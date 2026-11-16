package app.deskglow;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.ColorFilter;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PixelFormat;
import android.graphics.RectF;
import android.graphics.RadialGradient;
import android.graphics.Shader;
import android.graphics.drawable.Drawable;

final class SoftControls {
    static final int INK = 0xFF3D536B;
    static final int ACCENT = 0xFF2E6784;
    static final int ROSE = 0xFF96526B;
    static final int SURFACE = 0xFFEAF0F9;

    private SoftControls() { }

    static Drawable surface(Context context, int radius, boolean inset, boolean accented) {
        return new Surface(context.getResources().getDisplayMetrics().density, radius, inset, accented);
    }

    private static final class Surface extends Drawable {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF shape = new RectF();
        private final RectF inner = new RectF();
        private final float density;
        private final float radius;
        private final boolean inset;
        private final boolean accented;
        private boolean pressed;
        private Shader face;
        private Shader pressedFace;
        private Shader outerEdge;
        private Shader innerEdge;
        private Shader reflection;

        Surface(float density, int radius, boolean inset, boolean accented) {
            this.density = density;
            this.radius = radius * density;
            this.inset = inset;
            this.accented = accented;
        }

        @Override protected void onBoundsChange(android.graphics.Rect bounds) {
            shape.set(bounds);
            shape.inset(3 * density, 4 * density);
            if (shape.height() <= 0 || shape.width() <= 0) return;
            inner.set(shape);
            inner.inset(1.8f * density, 1.8f * density);
            face = new LinearGradient(shape.left, shape.top, shape.right, shape.bottom,
                    accented ? 0x27F4FAFF : 0x1CF3F7FD, 0x080B233A,
                    Shader.TileMode.CLAMP);
            pressedFace = new LinearGradient(shape.left, shape.top, shape.right, shape.bottom,
                    0x3514243F, 0x16F6FAFF, Shader.TileMode.CLAMP);
            float[] stops = {0f, .26f, .56f, 1f};
            outerEdge = new LinearGradient(shape.left, shape.top, shape.right, shape.bottom,
                    new int[]{0xF4FFFFFF, 0xB8F3FAFF, 0x273A526B, 0x99405265}, stops, Shader.TileMode.CLAMP);
            innerEdge = new LinearGradient(shape.left, shape.top, shape.right, shape.bottom,
                    new int[]{0x7B304257, 0x263D5870, 0x08FFFFFF, 0xA6FFFFFF}, stops, Shader.TileMode.CLAMP);
            reflection = new RadialGradient(shape.left + 2 * density, shape.top + density,
                    Math.max(1, Math.min(shape.width(), shape.height()) * .53f),
                    new int[]{0xA8FFFFFF, 0x44FFFFFF, 0x00FFFFFF},
                    new float[]{0f, .30f, 1f}, Shader.TileMode.CLAMP);
        }

        @Override public void draw(Canvas canvas) {
            if (shape.height() <= 0 || shape.width() <= 0) return;
            boolean recessed = inset || pressed;
            paint.setColor(0xFFFFFFFF);
            paint.setStyle(Paint.Style.FILL);
            paint.setShader(recessed ? pressedFace : face);
            canvas.drawRoundRect(shape, radius, radius, paint);
            if (!recessed) {
                paint.setShader(reflection);
                canvas.drawRoundRect(shape, radius, radius, paint);
            }
            paint.setStyle(Paint.Style.STROKE);
            paint.setStrokeWidth(2.1f * density);
            paint.setShader(recessed ? innerEdge : outerEdge);
            canvas.drawRoundRect(shape, radius, radius, paint);
            paint.setStrokeWidth(1.1f * density);
            paint.setShader(recessed ? outerEdge : innerEdge);
            canvas.drawRoundRect(inner, Math.max(0, radius - 2 * density),
                    Math.max(0, radius - 2 * density), paint);
            paint.setShader(null);
        }

        @Override public boolean isStateful() { return true; }
        @Override protected boolean onStateChange(int[] states) {
            boolean next = false;
            for (int state : states) if (state == android.R.attr.state_pressed) next = true;
            if (pressed == next) return false;
            pressed = next;
            invalidateSelf();
            return true;
        }
        @Override public void setAlpha(int alpha) { }
        @Override public void setColorFilter(ColorFilter filter) { }
        @Override public int getOpacity() { return PixelFormat.TRANSLUCENT; }
    }

    static void drawIcon(Canvas canvas, Paint paint, Path path, String action) {
        switch (action) {
            case "sleep":
                path.moveTo(13, 3);
                path.cubicTo(7, 3, 4, 7, 4, 12);
                path.cubicTo(4, 17, 8, 21, 13, 21);
                path.cubicTo(18, 21, 21, 17, 21, 13);
                path.cubicTo(13, 17, 8, 9, 13, 3);
                canvas.drawPath(path, paint);
                break;
            case "close-windows":
                canvas.drawRoundRect(4, 5, 20, 19, 2, 2, paint);
                canvas.drawLine(9, 9, 15, 15, paint);
                canvas.drawLine(15, 9, 9, 15, paint);
                break;
            case "chatgpt":
                path.moveTo(5, 5);
                path.lineTo(19, 5);
                path.quadTo(21, 5, 21, 7);
                path.lineTo(21, 15);
                path.quadTo(21, 17, 19, 17);
                path.lineTo(11, 17);
                path.lineTo(6, 21);
                path.lineTo(6, 17);
                path.quadTo(3, 17, 3, 15);
                path.lineTo(3, 7);
                path.quadTo(3, 5, 5, 5);
                canvas.drawPath(path, paint);
                for (int x = 8; x <= 16; x += 4) canvas.drawPoint(x, 11, paint);
                break;
            case "chrome":
                canvas.drawCircle(12, 12, 9, paint);
                canvas.drawCircle(12, 12, 3, paint);
                canvas.drawLine(12, 9, 20, 9, paint);
                canvas.drawLine(9.5f, 13.5f, 6, 5.5f, paint);
                canvas.drawLine(14.5f, 13.5f, 10, 21, paint);
                break;
            case "site":
                canvas.drawCircle(10, 10, 5.5f, paint);
                canvas.drawLine(14, 14, 20, 20, paint);
                break;
            default:
                canvas.drawRoundRect(3, 4, 21, 20, 2, 2, paint);
                canvas.drawLine(11, 4, 11, 20, paint);
                canvas.drawLine(11, 12, 21, 12, paint);
        }
    }
}
