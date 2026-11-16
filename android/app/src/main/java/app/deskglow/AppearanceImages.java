package app.deskglow;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.net.Uri;
import android.util.LruCache;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.UUID;

final class AppearanceImages {
    private final Context context;
    private final File directory;
    private final LruCache<String, Bitmap> blurPreviews = new LruCache<>(40);
    private final LruCache<String, Bitmap> cache = new LruCache<String, Bitmap>(24 * 1024 * 1024) {
        @Override protected int sizeOf(String key, Bitmap image) { return image.getAllocationByteCount(); }
    };

    AppearanceImages(Context context) {
        this.context = context;
        directory = new File(context.getFilesDir(), "appearance");
    }

    String importImage(Uri uri, int maxEdge) throws IOException {
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inJustDecodeBounds = true;
        try (InputStream stream = context.getContentResolver().openInputStream(uri)) {
            BitmapFactory.decodeStream(stream, null, options);
        }
        int edge = Math.max(options.outWidth, options.outHeight);
        if (edge <= 0) throw new IOException("이미지 형식을 읽을 수 없습니다.");
        options.inSampleSize = 1;
        while (edge / options.inSampleSize > maxEdge * 2) options.inSampleSize *= 2;
        options.inJustDecodeBounds = false;
        Bitmap bitmap;
        try (InputStream stream = context.getContentResolver().openInputStream(uri)) {
            bitmap = BitmapFactory.decodeStream(stream, null, options);
        }
        if (bitmap == null) throw new IOException("이미지를 열 수 없습니다.");
        int largest = Math.max(bitmap.getWidth(), bitmap.getHeight());
        if (largest > maxEdge) {
            Bitmap resized = Bitmap.createScaledBitmap(bitmap,
                    Math.max(1, bitmap.getWidth() * maxEdge / largest),
                    Math.max(1, bitmap.getHeight() * maxEdge / largest), true);
            bitmap.recycle();
            bitmap = resized;
        }
        if (!directory.isDirectory() && !directory.mkdirs()) throw new IOException("이미지 폴더를 만들 수 없습니다.");
        String id = UUID.randomUUID().toString();
        File file = new File(directory, id + ".png");
        try (FileOutputStream output = new FileOutputStream(file)) {
            if (!bitmap.compress(Bitmap.CompressFormat.PNG, 100, output)) throw new IOException("이미지를 저장할 수 없습니다.");
        } catch (IOException error) {
            file.delete();
            throw error;
        } finally {
            bitmap.recycle();
        }
        return id;
    }

    Bitmap get(String id) {
        if (id == null || id.isEmpty()) return null;
        Bitmap image = cache.get(id);
        if (image == null) {
            image = BitmapFactory.decodeFile(new File(directory, id + ".png").getPath());
            if (image != null) cache.put(id, image);
        }
        return image;
    }

    Bitmap getBlurPreview(String id, Bitmap image) {
        if (image == null) return null;
        Bitmap preview = blurPreviews.get(id);
        if (preview != null) return preview;
        int edge = Math.max(image.getWidth(), image.getHeight());
        preview = Bitmap.createScaledBitmap(image,
                Math.max(1, image.getWidth() * 12 / edge),
                Math.max(1, image.getHeight() * 12 / edge), true);
        blurPreviews.put(id, preview);
        return preview;
    }

    void delete(String id) {
        if (id == null || id.isEmpty()) return;
        cache.remove(id);
        blurPreviews.remove(id);
        new File(directory, id + ".png").delete();
    }
}
