package app.deskglow;

import androidx.activity.ComponentActivity;
import androidx.activity.result.ActivityResultLauncher;
import com.journeyapps.barcodescanner.ScanContract;
import com.journeyapps.barcodescanner.ScanOptions;
import android.os.Bundle;
import android.content.ClipData;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.drawable.GradientDrawable;
import android.view.Gravity;
import android.view.DragEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.*;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class MainActivity extends ComponentActivity {
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final ExecutorService imageWorker = Executors.newSingleThreadExecutor();
    private AppearanceImages images;
    private TextView status;
    private ImageView wallpaper;
    private ImageView wallpaperBlur;
    private View wallpaperShade;
    private Button appearanceButton;
    private Button wallpaperButton;
    private boolean editingAppearance;
    private static final int WALLPAPER = -1;
    private static final int NO_IMAGE_TARGET = -2;
    private int imageTarget = NO_IMAGE_TARGET;
    private final ActivityResultLauncher<String> imagePicker = registerForActivityResult(
            new androidx.activity.result.contract.ActivityResultContracts.GetContent(), uri -> {
                int target = imageTarget;
                imageTarget = NO_IMAGE_TARGET;
                if (uri == null || target == NO_IMAGE_TARGET) return;
                status.setText("이미지 적용 중…");
                imageWorker.execute(() -> {
                    try {
                        String id = images.importImage(uri, target == WALLPAPER ? 1600 : 384);
                        runOnUiThread(() -> {
                            if (isDestroyed()) {
                                images.delete(id);
                                return;
                            }
                            setImage(target, id);
                            status.setText("");
                        });
                    } catch (java.io.IOException | RuntimeException error) {
                        runOnUiThread(() -> {
                            if (!isDestroyed()) status.setText("이미지를 적용하지 못했습니다.");
                        });
                    }
                });
            });
    private final android.os.Handler clock = new android.os.Handler(android.os.Looper.getMainLooper());
    private final Runnable heartbeat = new Runnable() {
        @Override
        public void run() {
            send("check", null);
            clock.postDelayed(this, 10000);
        }
    };
    private EditText address;
    private EditText code;
    private TextView connectionStatus;
    private LinearLayout settings;
    private Button checkButton;
    private SeekBar volumeSlider;
    private TextView volumeValue;
    private MuteButton muteButton;
    private boolean draggingVolume;
    private boolean volumePending;
    private final FrameLayout[] slots = new FrameLayout[32];
    private final TileButton[] slotButtons = new TileButton[32];
    private final LinearLayout[] rows = new LinearLayout[8];
    private boolean draggingSlot;
    private ScrollView gridScroll;
    private int dragScrollDelta;
    private final Runnable dragScroll = new Runnable() {
        @Override public void run() {
            if (!draggingSlot || dragScrollDelta == 0) return;
            gridScroll.scrollBy(0, dragScrollDelta);
            clock.postDelayed(this, 40);
        }
    };
    private java.util.Map<String, String> workspaces = new java.util.LinkedHashMap<>();
    private org.json.JSONObject buttonNames = new org.json.JSONObject();
    private static final java.util.Map<String, String> ACTION_LABELS = new java.util.HashMap<>();
    static {
        ACTION_LABELS.put("chatgpt", "ChatGPT");
        ACTION_LABELS.put("chrome", "Chrome");
        ACTION_LABELS.put("site", "Google 열기");
        ACTION_LABELS.put("sleep", "PC 절전");
        ACTION_LABELS.put("close-windows", "창 모두 닫기");
    }
    private final String[] slotActions = new String[slots.length];
    private final ActivityResultLauncher<ScanOptions> scanner = registerForActivityResult(
            new ScanContract(), result -> {
                String payload = result.getContents();
                if (payload == null) return;
                java.util.regex.Matcher match = java.util.regex.Pattern
                        .compile("deskglow:(http://[0-9.]+:8766)#([0-9A-Fa-f]{24})")
                        .matcher(payload);
                if (!match.matches()) {
                    status.setText("DeskGlow 페어링 QR을 스캔하세요.");
                    return;
                }
                address.setText(match.group(1));
                code.setText(match.group(2));
                send("check", checkButton);
            });

    @Override
    public void onCreate(Bundle state) {
        super.onCreate(state);
        images = new AppearanceImages(this);
        if (state != null) {
            imageTarget = state.getInt("image-target", NO_IMAGE_TARGET);
            editingAppearance = state.getBoolean("appearance-mode");
        }
        FrameLayout root = new FrameLayout(this);
        root.setBackground(defaultBackdrop());
        wallpaperBlur = new ImageView(this);
        root.addView(wallpaperBlur, new FrameLayout.LayoutParams(-1, -1));
        wallpaper = new ImageView(this);
        wallpaper.setScaleType(ImageView.ScaleType.FIT_CENTER);
        root.addView(wallpaper, new FrameLayout.LayoutParams(-1, -1));
        wallpaperShade = new View(this);
        wallpaperShade.setBackgroundColor(0x08EAF0F9);
        root.addView(wallpaperShade, new FrameLayout.LayoutParams(-1, -1));
        refreshWallpaper();
        LinearLayout screen = new LinearLayout(this);
        screen.setOrientation(LinearLayout.VERTICAL);
        screen.setPadding(dp(18), dp(7), dp(18), dp(22));
        LinearLayout header = new LinearLayout(this);
        header.setGravity(android.view.Gravity.CENTER_VERTICAL);
        header.setPadding(dp(4), 0, dp(5), dp(5));
        screen.addView(header);
        connectionStatus = new TextView(this);
        connectionStatus.setTextSize(15);
        connectionStatus.setSingleLine(true);
        connectionStatus.setEllipsize(android.text.TextUtils.TruncateAt.END);
        LinearLayout statusGroup = new LinearLayout(this);
        statusGroup.setGravity(Gravity.CENTER_VERTICAL);
        header.addView(statusGroup, new LinearLayout.LayoutParams(0, dp(42), 1));
        statusGroup.addView(connectionStatus, new LinearLayout.LayoutParams(0, -2, 1));
        connectionStatus.setPadding(dp(4), dp(8), dp(3), dp(8));
        TextView version = new TextView(this);
        version.setText("v" + BuildConfig.VERSION_NAME);
        version.setTextSize(10);
        version.setSingleLine(true);
        applyReadableText(version, 0xFFE0E8F1);
        statusGroup.addView(version, new LinearLayout.LayoutParams(-2, -2));
        Button settingsButton = new Button(this);
        settingsButton.setText("연결 설정");
        addHeaderButton(header, settingsButton);
        wallpaperButton = new Button(this);
        wallpaperButton.setText("배경");
        wallpaperButton.setOnClickListener(view -> showImageOptions(WALLPAPER));
        addHeaderButton(header, wallpaperButton);
        appearanceButton = new Button(this);
        addHeaderButton(header, appearanceButton);
        appearanceButton.setOnClickListener(view -> setAppearanceMode(!editingAppearance));

        settings = new LinearLayout(this);
        settings.setGravity(Gravity.CENTER_VERTICAL);
        screen.addView(settings);
        settingsButton.setOnClickListener(view -> settings.setVisibility(
                settings.getVisibility() == android.view.View.VISIBLE
                        ? android.view.View.GONE : android.view.View.VISIBLE));
        address = field(settings, "http://PC주소:8766", "address");
        address.setInputType(17);
        code = field(settings, "페어링 코드", "code");
        code.setInputType(129);
        checkButton = new Button(this);
        checkButton.setText("연결 확인");
        addHeaderButton(settings, checkButton);
        checkButton.setOnClickListener(view -> send("check", checkButton));
        Button scanButton = new Button(this);
        scanButton.setText("QR로 연결");
        addHeaderButton(settings, scanButton);
        styleHeaderButton(scanButton, true);
        scanButton.setOnClickListener(view -> scanner.launch(new ScanOptions()
                .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                .setPrompt("Windows의 DeskGlow QR을 스캔하세요.")
                .setBeepEnabled(false)));
        status = new TextView(this);
        applyReadableText(status, 0xFFF5F8FC);
        status.setPadding(dp(12), 0, dp(12), 0);
        screen.addView(status);
        LinearLayout controls = new LinearLayout(this);
        screen.addView(controls, new LinearLayout.LayoutParams(-1, 0, 1));
        LinearLayout grid = new LinearLayout(this);
        grid.setOrientation(LinearLayout.VERTICAL);
        gridScroll = new ScrollView(this);
        gridScroll.setFillViewport(true);
        gridScroll.addView(grid);
        controls.addView(gridScroll, new LinearLayout.LayoutParams(0, -1, 1));
        loadSlots();
        for (int row = 0; row < rows.length; row++) {
            LinearLayout line = new LinearLayout(this);
            rows[row] = line;
            grid.addView(line, new LinearLayout.LayoutParams(-1, dp(83), 1));
            for (int column = 0; column < 4; column++) {
                int position = row * 4 + column;
                FrameLayout slot = new FrameLayout(this);
                slots[position] = slot;
                LinearLayout.LayoutParams size = new LinearLayout.LayoutParams(0, -1, 1);
                size.setMargins(dp(5), dp(5), dp(5), dp(5));
                line.addView(slot, size);
                slot.setOnDragListener((view, event) -> dragSlot(position, event));
                renderSlot(position);
            }
        }
        showUsedRows();
        controls.addView(createVolumePanel(), new LinearLayout.LayoutParams(dp(112), -1));
        root.addView(screen, new FrameLayout.LayoutParams(-1, -1));
        if (android.os.Build.VERSION.SDK_INT >= 35) {
            androidx.core.view.ViewCompat.setOnApplyWindowInsetsListener(root, (view, insets) -> {
                androidx.core.graphics.Insets safe = insets.getInsets(
                        androidx.core.view.WindowInsetsCompat.Type.systemBars() |
                                androidx.core.view.WindowInsetsCompat.Type.displayCutout());
                screen.setPadding(dp(18) + safe.left, dp(7) + safe.top,
                        dp(18) + safe.right, dp(22) + safe.bottom);
                return insets;
            });
        }
        setContentView(root);
        setAppearanceMode(editingAppearance);
        boolean paired = !address.getText().toString().isEmpty() && !code.getText().toString().isEmpty();
        settings.setVisibility(paired ? android.view.View.GONE : android.view.View.VISIBLE);
        if (paired) {
            showConnection("PC 확인 중", 0xFFFFE6AA);
        } else {
            showConnection("PC 연결 필요", 0xFFF5F8FC);
        }
    }

    @Override
    protected void onStart() {
        super.onStart();
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        clock.post(heartbeat);
    }

    @Override
    protected void onStop() {
        getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        clock.removeCallbacks(heartbeat);
        clock.removeCallbacks(dragScroll);
        super.onStop();
    }

    @Override
    protected void onDestroy() {
        worker.shutdownNow();
        imageWorker.shutdown();
        super.onDestroy();
    }

    @Override
    protected void onSaveInstanceState(Bundle state) {
        super.onSaveInstanceState(state);
        state.putInt("image-target", imageTarget);
        state.putBoolean("appearance-mode", editingAppearance);
    }

    private void setAppearanceMode(boolean enabled) {
        editingAppearance = enabled;
        appearanceButton.setText(enabled ? "완료" : "꾸미기");
        styleHeaderButton(appearanceButton, enabled);
        wallpaperButton.setVisibility(enabled ? View.VISIBLE : View.GONE);
        for (int index = 0; index < slots.length; index++) renderSlot(index);
    }

    private String imageKey(int target) {
        return target == WALLPAPER ? "wallpaper-image" : "slot-image-" + target;
    }

    private String imageId(int target) {
        return getPreferences(MODE_PRIVATE).getString(imageKey(target), "");
    }

    private void refreshWallpaper() {
        String id = imageId(WALLPAPER);
        android.graphics.Bitmap image = images.get(id);
        showBlurredImage(wallpaperBlur, id, image, 28);
        wallpaper.setImageBitmap(image);
        wallpaperShade.setVisibility(image == null ? View.GONE : View.VISIBLE);
    }

    private void showBlurredImage(ImageView view, String id, android.graphics.Bitmap image, int radius) {
        view.setScaleType(ImageView.ScaleType.CENTER_CROP);
        if (android.os.Build.VERSION.SDK_INT >= 31) {
            view.setImageBitmap(image);
            if (image != null) view.setRenderEffect(android.graphics.RenderEffect.createBlurEffect(
                    dp(radius), dp(radius), Shader.TileMode.CLAMP));
        } else {
            view.setImageBitmap(images.getBlurPreview(id, image));
        }
    }

    private android.graphics.drawable.Drawable defaultBackdrop() {
        return new GradientDrawable(GradientDrawable.Orientation.TL_BR,
                new int[]{0xFFFAFCFF, SoftControls.SURFACE, 0xFFDCE7F5});
    }

    private void addHeaderButton(LinearLayout header, Button button) {
        LinearLayout.LayoutParams size = new LinearLayout.LayoutParams(-2, dp(40));
        size.leftMargin = dp(10);
        header.addView(button, size);
        button.setAllCaps(false);
        button.setTextSize(12);
        button.setPadding(dp(11), 0, dp(11), 0);
        button.setMinWidth(dp(48));
        button.setMinimumWidth(dp(48));
        styleHeaderButton(button, false);
    }

    private void styleHeaderButton(Button button, boolean accented) {
        applyReadableText(button, accented ? 0xFFD6F2FF : 0xFFF5F8FC);
        button.setBackground(SoftControls.surface(this, 15, accented, accented));
    }

    private void showImageOptions(int target) {
        boolean hasImage = !imageId(target).isEmpty();
        java.util.List<String> options = new java.util.ArrayList<>();
        options.add("사진 선택");
        if (hasImage) options.add(target == WALLPAPER ? "기본 배경으로 복원" : "이미지 지우기");
        String action = target == WALLPAPER ? "" : slotActions[target];
        if (!action.isEmpty()) options.add("버튼 이름 바꾸기");
        new android.app.AlertDialog.Builder(this)
                .setTitle(target == WALLPAPER ? "배경 이미지" : "버튼 꾸미기")
                .setItems(options.toArray(new String[0]), (dialog, choice) -> {
                    if (choice == 0) {
                        imageTarget = target;
                        imagePicker.launch("image/*");
                    } else if (hasImage && choice == 1) {
                        setImage(target, "");
                    } else {
                        editButtonName(action);
                    }
                }).show();
    }

    private String buttonLabel(String action) {
        String fallback = workspaces.containsKey(action) ? workspaces.get(action) : ACTION_LABELS.get(action);
        return buttonNames.optString(action, fallback);
    }

    private void editButtonName(String action) {
        EditText field = new EditText(this);
        field.setSingleLine(true);
        field.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(32)});
        field.setText(buttonLabel(action));
        field.selectAll();
        new android.app.AlertDialog.Builder(this)
                .setTitle("버튼 이름 바꾸기")
                .setView(field)
                .setNegativeButton("취소", null)
                .setNeutralButton("기본 이름", (dialog, which) -> renameButton(action, ""))
                .setPositiveButton("저장", (dialog, which) -> renameButton(action, field.getText().toString()))
                .show();
    }

    private void renameButton(String action, String name) {
        String host = getPreferences(MODE_PRIVATE).getString("address", "");
        String token = getPreferences(MODE_PRIVATE).getString("code", "");
        if (host.isEmpty() || token.isEmpty()) {
            status.setText("PC를 연결한 뒤 이름을 바꿔 주세요.");
            return;
        }
        worker.execute(() -> {
            int result = -1;
            HttpURLConnection connection = null;
            try {
                connection = (HttpURLConnection) new URL(host.replaceAll("/$", "") + "/buttons/name").openConnection();
                connection.setInstanceFollowRedirects(false);
                connection.setRequestMethod("POST");
                connection.setRequestProperty("Authorization", "Bearer " + token.toUpperCase(java.util.Locale.ROOT));
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                connection.setConnectTimeout(3000);
                connection.setReadTimeout(3000);
                connection.setDoOutput(true);
                byte[] body = new org.json.JSONObject().put("id", action).put("name", name)
                        .toString().getBytes(java.nio.charset.StandardCharsets.UTF_8);
                connection.setFixedLengthStreamingMode(body.length);
                try (java.io.OutputStream output = connection.getOutputStream()) { output.write(body); }
                result = connection.getResponseCode();
            } catch (java.io.IOException | org.json.JSONException ignored) {
                // The status message is shown on the main thread.
            } finally {
                if (connection != null) connection.disconnect();
            }
            int response = result;
            runOnUiThread(() -> {
                if (isDestroyed()) return;
                status.setText(response == 204 ? "버튼 이름을 저장했습니다." :
                        response == 404 ? "Windows DeskGlow를 업데이트하세요." :
                                "이름을 저장하지 못했습니다. PC 연결과 버튼 이름을 확인하세요.");
                if (response == 204) send("check", null);
            });
        });
    }

    private void updateButtonNames(String value) {
        if (value == null) return;
        try {
            org.json.JSONObject names = new org.json.JSONObject(new String(
                    android.util.Base64.decode(value, android.util.Base64.DEFAULT),
                    java.nio.charset.StandardCharsets.UTF_8));
            if (names.toString().equals(buttonNames.toString())) return;
            buttonNames = names;
            getPreferences(MODE_PRIVATE).edit().putString("button-names", names.toString()).apply();
            for (int index = 0; index < slotButtons.length; index++) {
                if (slotButtons[index] != null && !slotActions[index].isEmpty())
                    slotButtons[index].setText(buttonLabel(slotActions[index]));
            }
        } catch (IllegalArgumentException | org.json.JSONException ignored) {
            // Retain the last valid names when the catalog cannot be parsed.
        }
    }

    private void setImage(int target, String id) {
        String previous = imageId(target);
        getPreferences(MODE_PRIVATE).edit().putString(imageKey(target), id).apply();
        images.delete(previous);
        if (target == WALLPAPER) refreshWallpaper();
        else {
            renderSlot(target);
            showUsedRows();
        }
    }

    private EditText field(LinearLayout row, String hint, String key) {
        EditText field = new EditText(this);
        field.setSingleLine(true);
        field.setHint(hint);
        field.setBackground(SoftControls.surface(this, 14, true, false));
        field.setPadding(dp(12), dp(8), dp(12), dp(8));
        field.setTextColor(SoftControls.INK);
        field.setHintTextColor(0xFF566B82);
        field.setText(getPreferences(MODE_PRIVATE).getString(key, ""));
        row.addView(field, new LinearLayout.LayoutParams(0, -2, 1));
        return field;
    }

    private LinearLayout createVolumePanel() {
        LinearLayout panel = new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setGravity(android.view.Gravity.CENTER_HORIZONTAL);
        volumeValue = new TextView(this);
        volumeValue.setTextSize(15);
        applyReadableText(volumeValue, 0xFFF5F8FC);
        volumeValue.setGravity(Gravity.CENTER);
        FrameLayout capsule = new FrameLayout(this);
        panel.addView(capsule, new LinearLayout.LayoutParams(dp(54), 0, 1));
        volumeSlider = new CapsuleSeekBar(this);
        volumeSlider.setMax(100);
        volumeSlider.setContentDescription("PC 볼륨");
        capsule.addView(new VerticalSlider(this, volumeSlider), new FrameLayout.LayoutParams(-1, -1));
        FrameLayout.LayoutParams number = new FrameLayout.LayoutParams(-1, dp(44), Gravity.TOP);
        capsule.addView(volumeValue, number);
        muteButton = new MuteButton(this);
        muteButton.setPadding(0, 0, 0, 0);
        FrameLayout.LayoutParams mute = new FrameLayout.LayoutParams(dp(48), dp(44), Gravity.BOTTOM | Gravity.CENTER_HORIZONTAL);
        mute.bottomMargin = dp(20);
        capsule.addView(muteButton, mute);
        muteButton.setOnClickListener(view -> sendVolume("toggle-mute"));
        volumeSlider.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onStartTrackingTouch(SeekBar slider) { draggingVolume = true; }
            @Override public void onStopTrackingTouch(SeekBar slider) {
                draggingVolume = false;
                sendVolume("volume-" + slider.getProgress());
            }
            @Override public void onProgressChanged(SeekBar slider, int level, boolean fromUser) {
                slider.invalidate();
                if (!fromUser) return;
                volumeValue.setText(level + "%");
                if (!draggingVolume) sendVolume("volume-" + level);
            }
        });
        updateVolume(null, false);
        return panel;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private void loadSlots() {
        try {
            buttonNames = new org.json.JSONObject(getPreferences(MODE_PRIVATE).getString("button-names", "{}"));
        } catch (org.json.JSONException ignored) { }
        java.util.Arrays.fill(slotActions, "");
        String catalog = getPreferences(MODE_PRIVATE).getString("workspace-catalog", "");
        java.util.Map<String, String> cached = parseWorkspaces(catalog);
        if (cached != null) workspaces = cached;
        else workspaces.put("workspace", "작업 배치");
        String order = getPreferences(MODE_PRIVATE).getString("grid-order", "workspace,chatgpt,chrome,site,sleep");
        String[] saved = order.split(",", -1);
        System.arraycopy(saved, 0, slotActions, 0, Math.min(saved.length, slotActions.length));
        reconcileSlots();
    }

    private void reconcileSlots() {
        java.util.Set<String> actions = new java.util.HashSet<>();
        for (int index = 0; index < slotActions.length; index++) {
            String action = slotActions[index];
            if (!(ACTION_LABELS.containsKey(action) || workspaces.containsKey(action)) || !actions.add(action))
                slotActions[index] = "";
        }
        java.util.List<String> required = new java.util.ArrayList<>(
                java.util.Arrays.asList("chatgpt", "chrome", "site", "sleep", "close-windows"));
        required.addAll(workspaces.keySet());
        int next = 0;
        for (String action : required) {
            if (actions.contains(action)) continue;
            while (next < slotActions.length && !slotActions[next].isEmpty()) next++;
            if (next == slotActions.length) break;
            slotActions[next++] = action;
        }
    }

    private void showUsedRows() {
        int last = slotActions.length - 1;
        while (last >= 8 && slotActions[last].isEmpty() && imageId(last).isEmpty()) last--;
        for (int index = 0; index < rows.length; index++)
            rows[index].setVisibility(index <= last / 4 ? View.VISIBLE : View.GONE);
    }

    private java.util.Map<String, String> parseWorkspaces(String header) {
        if (header == null || header.isEmpty()) return null;
        try {
            byte[] data = android.util.Base64.decode(header, android.util.Base64.DEFAULT);
            org.json.JSONArray list = new org.json.JSONArray(
                    new String(data, java.nio.charset.StandardCharsets.UTF_8));
            if (list.length() > 24) return null;
            java.util.Map<String, String> presets = new java.util.LinkedHashMap<>();
            for (int index = 0; index < list.length(); index++) {
                org.json.JSONObject entry = list.getJSONObject(index);
                String id = entry.getString("id");
                String name = entry.getString("name");
                if (!id.matches("workspace(?:-[0-9a-f]{32})?") || name.isEmpty() || name.length() > 32)
                    return null;
                presets.put(id, name);
            }
            return presets;
        } catch (IllegalArgumentException | org.json.JSONException error) {
            return null;
        }
    }

    private void updateWorkspaces(String header) {
        if (draggingSlot) return;
        java.util.Map<String, String> incoming = parseWorkspaces(header);
        if (incoming == null || incoming.equals(workspaces)) return;
        workspaces = incoming;
        reconcileSlots();
        getPreferences(MODE_PRIVATE).edit().putString("workspace-catalog", header)
                .putString("grid-order", String.join(",", slotActions)).apply();
        for (int index = 0; index < slots.length; index++) renderSlot(index);
        showUsedRows();
    }

    private android.graphics.drawable.GradientDrawable slotBackground(boolean highlighted) {
        android.graphics.drawable.GradientDrawable drawable = new android.graphics.drawable.GradientDrawable();
        drawable.setCornerRadius(dp(10));
        drawable.setColor(highlighted ? 0x406FAAC8 : 0x00000000);
        drawable.setStroke(dp(1), 0xA04A7D9A);
        return drawable;
    }

    private void renderSlot(int position) {
        FrameLayout slot = slots[position];
        slot.removeAllViews();
        slotButtons[position] = null;
        String action = slotActions[position];
        slot.setBackground(SoftControls.surface(this, 20, action.isEmpty(), "sleep".equals(action)));
        android.graphics.Bitmap image = images.get(imageId(position));
        if (image != null) {
            ImageView blurred = new ImageView(this);
            showBlurredImage(blurred, imageId(position), image, 12);
            FrameLayout.LayoutParams blurSize = new FrameLayout.LayoutParams(-1, -1);
            int bottom = action.isEmpty() ? dp(11) : dp(29);
            blurSize.setMargins(dp(12), dp(10), dp(12), bottom);
            slot.addView(blurred, blurSize);
            ImageView art = new ImageView(this);
            art.setImageBitmap(image);
            art.setScaleType(ImageView.ScaleType.FIT_CENTER);
            FrameLayout.LayoutParams artSize = new FrameLayout.LayoutParams(-1, -1);
            artSize.setMargins(dp(12), dp(10), dp(12), bottom);
            slot.addView(art, artSize);
        }
        if (action.isEmpty() && !editingAppearance && image == null) return;
        String icon = image == null && !action.isEmpty() ? action : null;
        TileButton tile = new TileButton(this, icon);
        String label = buttonLabel(action);
        tile.setText(action.isEmpty() ? (editingAppearance ? "+" : "") : label);
        applyReadableText(tile, "sleep".equals(action) || "close-windows".equals(action)
                ? 0xFFFFDFE6 : 0xFFF5F8FC);
        tile.setTypeface(null, android.graphics.Typeface.NORMAL);
        tile.setPadding(dp(7), dp(icon == null ? 6 : 27), dp(7), dp(7));
        tile.setTextSize(13);
        tile.setBackground(new android.graphics.drawable.RippleDrawable(
                android.content.res.ColorStateList.valueOf(0x406B93B4), null,
                SoftControls.surface(this, 20, false, false)));
        if (image != null && !action.isEmpty()) tile.setGravity(Gravity.BOTTOM | Gravity.CENTER_HORIZONTAL);
        tile.setMaxLines(2);
        tile.setEllipsize(android.text.TextUtils.TruncateAt.END);
        tile.setAllCaps(false);
        if (action.isEmpty()) {
            tile.setClickable(editingAppearance);
            if (editingAppearance) tile.setContentDescription("빈 슬롯 이미지 설정");
        }
        if (editingAppearance || !action.isEmpty())
            tile.setOnClickListener(view -> {
                if (editingAppearance) showImageOptions(position);
                else activate(action, tile);
            });
        if (!action.isEmpty() || image != null) tile.setOnLongClickListener(view -> {
            draggingSlot = tile.startDragAndDrop(
                    ClipData.newPlainText("DeskGlow", ""), new View.DragShadowBuilder(tile), position, 0);
            return draggingSlot;
        });
        slot.addView(tile, new FrameLayout.LayoutParams(-1, -1));
        slotButtons[position] = tile;
    }

    private static final class TileButton extends Button {
        private final Paint iconPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final android.graphics.Path iconPath = new android.graphics.Path();
        private final String icon;

        TileButton(android.content.Context context, String icon) {
            super(context);
            this.icon = icon;
            iconPaint.setStyle(Paint.Style.STROKE);
            iconPaint.setStrokeWidth(1.65f);
            iconPaint.setStrokeCap(Paint.Cap.ROUND);
            iconPaint.setStrokeJoin(Paint.Join.ROUND);
        }

        @Override protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            if (icon == null) return;
            float scale = getResources().getDisplayMetrics().density * .90f;
            canvas.save();
            canvas.translate(getWidth() / 2f - 12 * scale,
                    Math.max(5 * scale, getTotalPaddingTop() - 27 * scale));
            canvas.scale(scale, scale);
            iconPaint.setColor(isEnabled() ? getCurrentTextColor() : 0xFF8998AA);
            iconPaint.setShadowLayer(1.8f, 0, 0, 0xFF142033);
            iconPath.reset();
            SoftControls.drawIcon(canvas, iconPaint, iconPath, icon);
            canvas.restore();
        }
    }

    private boolean dragSlot(int destination, DragEvent event) {
        if (!(event.getLocalState() instanceof Integer)) return false;
        int source = (Integer) event.getLocalState();
        switch (event.getAction()) {
            case DragEvent.ACTION_DRAG_ENTERED:
                slots[destination].setForeground(slotBackground(true));
                break;
            case DragEvent.ACTION_DRAG_LOCATION:
                int[] slotOrigin = new int[2];
                int[] viewportOrigin = new int[2];
                slots[destination].getLocationOnScreen(slotOrigin);
                gridScroll.getLocationOnScreen(viewportOrigin);
                float y = slotOrigin[1] - viewportOrigin[1] + event.getY();
                int delta = y < dp(32) ? -dp(12) :
                        y > gridScroll.getHeight() - dp(32) ? dp(12) : 0;
                if (delta != dragScrollDelta) {
                    dragScrollDelta = delta;
                    clock.removeCallbacks(dragScroll);
                    if (delta != 0) clock.post(dragScroll);
                }
                break;
            case DragEvent.ACTION_DRAG_EXITED:
                slots[destination].setForeground(null);
                dragScrollDelta = 0;
                break;
            case DragEvent.ACTION_DRAG_ENDED:
                draggingSlot = false;
                dragScrollDelta = 0;
                slots[destination].setForeground(null);
                showUsedRows();
                break;
            case DragEvent.ACTION_DROP:
                if (source != destination) {
                    String previous = slotActions[destination];
                    slotActions[destination] = slotActions[source];
                    slotActions[source] = previous;
                    String sourceImage = imageId(source);
                    String destinationImage = imageId(destination);
                    getPreferences(MODE_PRIVATE).edit()
                            .putString(imageKey(source), destinationImage)
                            .putString(imageKey(destination), sourceImage)
                            .putString("grid-order", String.join(",", slotActions)).apply();
                    renderSlot(source);
                    renderSlot(destination);
                }
                break;
        }
        return true;
    }

    private void sendVolume(String action) {
        if (volumePending) return;
        volumePending = true;
        volumeSlider.setEnabled(false);
        send(action, muteButton);
    }

    private void updateVolume(String level, boolean muted) {
        if (draggingVolume || volumePending) return;
        boolean available = level != null && level.matches("100|[0-9]{1,2}");
        volumeSlider.setEnabled(available);
        muteButton.setEnabled(available);
        muteButton.setMuted(available && muted);
        muteButton.setContentDescription(available && muted ? "음소거 해제" : "음소거");
        volumeSlider.setProgress(available ? Integer.parseInt(level) : 0);
        volumeValue.setText(available ? level + "%" : "—");
    }

    private void activate(String action, Button button) {
        if (!"close-windows".equals(action)) {
            send(action, button);
            return;
        }
        new android.app.AlertDialog.Builder(this)
                .setTitle("PC에서 열린 앱 창을 모두 닫을까요?")
                .setMessage("열린 창에 닫기 요청을 보냅니다. 저장 확인이 뜨면 PC에서 처리하세요.")
                .setNegativeButton("취소", null)
                .setPositiveButton("창 닫기", (dialog, which) -> send(action, button))
                .show();
    }

    private void send(String action, Button button) {
        boolean backgroundCheck = button == null;
        boolean audioAction = action.startsWith("volume-") || "toggle-mute".equals(action);
        boolean closingWindows = "close-windows".equals(action);
        boolean workspaceAction = action.startsWith("workspace") || "chrome".equals(action);
        boolean useSavedConnection = backgroundCheck || audioAction;
        String host = useSavedConnection ? getPreferences(MODE_PRIVATE).getString("address", "") :
                address.getText().toString().trim();
        String token = useSavedConnection ? getPreferences(MODE_PRIVATE).getString("code", "") :
                code.getText().toString().trim();
        if (!host.matches("http://[0-9.]+:8766/?") || !token.matches("[0-9A-Fa-f]{24}")) {
            if (audioAction) {
                volumePending = false;
                updateVolume(null, false);
            }
            if (!backgroundCheck) status.setText("PC 주소와 24자리 페어링 코드를 확인하세요.");
            return;
        }
        if (!backgroundCheck) {
            button.setEnabled(false);
            status.setText("전송 중");
        }
        worker.execute(() -> {
            String message;
            HttpURLConnection connection = null;
            boolean connected = false;
            int responseCode = -1;
            String levelHeader = null;
            boolean mutedHeader = false;
            String workspacesHeader = null;
            String namesHeader = null;
            try {
                URL url = new URL(host.replaceAll("/$", "") + "/action/" + action);
                connection = (HttpURLConnection) url.openConnection();
                connection.setInstanceFollowRedirects(false);
                connection.setRequestMethod("POST");
                connection.setRequestProperty("Authorization", "Bearer " +
                        token.toUpperCase(java.util.Locale.ROOT));
                connection.setConnectTimeout(3000);
                connection.setReadTimeout(3000);
                int result = connection.getResponseCode();
                responseCode = result;
                levelHeader = connection.getHeaderField("X-DeskGlow-Volume");
                mutedHeader = "1".equals(connection.getHeaderField("X-DeskGlow-Muted"));
                workspacesHeader = connection.getHeaderField("X-DeskGlow-Workspaces");
                namesHeader = connection.getHeaderField("X-DeskGlow-Button-Names");
                connected = result == 204 || result == 202;
                message = result == 202 ? ("sleep".equals(action) ?
                        "절전 요청을 보냈어요. 다시 사용하려면 PC에 연결된 키보드의 키를 누르거나 본체 전원 버튼을 짧게 눌러 주세요." :
                        "chatgpt".equals(action) ? "PC에 ChatGPT 열기를 요청했습니다." :
                                "PC에 창 복원을 요청했습니다. 결과는 Windows의 작업 배치 탭에 표시됩니다.") :
                        connected ? ("check".equals(action) || audioAction ? "" : "명령 전송 완료") :
                        result == 404 && workspaceAction ? "Windows의 작업 배치에서 복원할 창을 저장하세요." :
                        result == 409 && (workspaceAction || "chatgpt".equals(action)) ? "PC에서 다른 창 요청을 처리 중입니다." :
                        result == 404 && ("sleep".equals(action) || audioAction || closingWindows) ? "Windows DeskGlow를 업데이트하세요." :
                        result == 422 && audioAction ? "PC의 기본 오디오 출력 장치를 확인하세요." :
                        result == 503 ? "Windows에서 PC 제어가 꺼져 있습니다." :
                        result == 401 ? "Windows에 표시된 새 코드를 확인하세요." : "명령 실행 실패";
                if (closingWindows && connected) {
                    int requested = connection.getHeaderFieldInt("X-DeskGlow-Close-Requested", 0);
                    int blocked = connection.getHeaderFieldInt("X-DeskGlow-Close-Blocked", 0);
                    int awaitingApproval = connection.getHeaderFieldInt("X-DeskGlow-Close-Approval", 0);
                    message = awaitingApproval > 0 ? "관리자 PowerShell을 닫으려면 PC에서 승인하세요." :
                            requested == 0 && blocked == 0 ? "닫을 앱 창이 없습니다." :
                            "창 " + requested + "개에 닫기 요청" + (blocked > 0 ? " · 전송 실패 " + blocked + "개" : "");
                }
            } catch (java.io.IOException error) {
                message = "PC 주소, Wi-Fi 및 Windows 방화벽을 확인하세요.";
            } finally {
                if (connection != null) connection.disconnect();
            }
            String feedback = message;
            boolean success = connected;
            int response = responseCode;
            String level = levelHeader;
            boolean muted = mutedHeader;
            String catalog = workspacesHeader;
            String aliases = namesHeader;
            runOnUiThread(() -> {
                if (isDestroyed()) return;
                if (backgroundCheck) {
                    if (host.equals(getPreferences(MODE_PRIVATE).getString("address", "")) &&
                            token.equals(getPreferences(MODE_PRIVATE).getString("code", ""))) {
                        updateConnection(response, success);
                        updateVolume(level, muted);
                        updateWorkspaces(catalog);
                        updateButtonNames(aliases);
                    }
                    return;
                }
                if (success) getPreferences(MODE_PRIVATE).edit()
                        .putString("address", host).putString("code", token).apply();
                updateConnection(response, success);
                if (success && "check".equals(action)) settings.setVisibility(android.view.View.GONE);
                status.setText(feedback);
                button.setEnabled(true);
                if (audioAction) volumePending = false;
                if (audioAction || "check".equals(action)) updateVolume(level, muted);
                if ("check".equals(action)) updateWorkspaces(catalog);
                if ("check".equals(action)) updateButtonNames(aliases);
            });
        });
    }

    private static final class MuteButton extends Button {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final android.graphics.Path speaker = new android.graphics.Path();
        private boolean muted;

        MuteButton(android.content.Context context) {
            super(context);
            setBackground(new android.graphics.drawable.RippleDrawable(
                    android.content.res.ColorStateList.valueOf(0x33BAD6FF), null,
                    new android.graphics.drawable.ColorDrawable(0xFFFFFFFF)));
            speaker.moveTo(4, 9);
            speaker.lineTo(7, 9);
            speaker.lineTo(12, 5);
            speaker.lineTo(12, 19);
            speaker.lineTo(7, 15);
            speaker.lineTo(4, 15);
            speaker.close();
        }

        void setMuted(boolean value) {
            if (muted == value) return;
            muted = value;
            invalidate();
        }

        @Override protected void onDraw(Canvas canvas) {
            float density = getResources().getDisplayMetrics().density;
            canvas.save();
            canvas.translate(getWidth() / 2f, getHeight() / 2f);
            canvas.scale(density * 0.85f, density * 0.85f);
            paint.setStyle(Paint.Style.STROKE);
            canvas.translate(-12, -12);
            paint.setColor(isEnabled() ? SoftControls.INK : 0xFF8998AA);
            paint.setStrokeWidth(1.5f);
            paint.setStrokeCap(Paint.Cap.ROUND);
            paint.setStrokeJoin(Paint.Join.ROUND);
            canvas.drawPath(speaker, paint);
            if (muted) {
                canvas.drawLine(15, 9, 20, 15, paint);
                canvas.drawLine(20, 9, 15, 15, paint);
            } else {
                canvas.drawArc(10, 7, 20, 17, -43, 86, false, paint);
                canvas.drawArc(8, 4, 23, 20, -43, 86, false, paint);
            }
            canvas.restore();
        }
    }

    private static final class CapsuleSeekBar extends SeekBar {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF bounds = new RectF();
        private final android.graphics.Path shape = new android.graphics.Path();
        private Shader gradient;
        private Shader crossLight;
        private Shader track;

        CapsuleSeekBar(android.content.Context context) {
            super(context);
            setThumb(null);
            setSplitTrack(false);
            setPadding(0, 0, 0, 0);
            setProgressDrawable(new android.graphics.drawable.ColorDrawable(0x00000000));
        }

        @Override protected void onSizeChanged(int width, int height, int oldWidth, int oldHeight) {
            super.onSizeChanged(width, height, oldWidth, oldHeight);
            bounds.set(0, 0, width, height);
            float radius = Math.min(width, height) / 2f;
            shape.reset();
            shape.addRoundRect(bounds, radius, radius, android.graphics.Path.Direction.CW);
            if (width > 0 && height > 0) {
                gradient = new LinearGradient(0, 0, width, 0,
                        new int[]{0xBB9ABBDD, 0x89B5CBE4, 0x68C0D2E9, 0x60D2DFF0},
                        new float[]{0f, .4f, .75f, 1f}, Shader.TileMode.CLAMP);
                crossLight = new LinearGradient(0, 0, 0, height,
                        new int[]{0x14FFFFFF, 0x4AFFFFFF, 0x16FFFFFF, 0x00FFFFFF, 0x23849CB9},
                        new float[]{0f, .20f, .47f, .66f, 1f}, Shader.TileMode.CLAMP);
                track = new LinearGradient(0, 0, 0, height,
                        new int[]{0x59D0DDED, 0x22EDF3FC, 0x51D6E2F1}, null, Shader.TileMode.CLAMP);
            }
        }

        @Override protected synchronized void onDraw(Canvas canvas) {
            canvas.save();
            canvas.clipPath(shape);
            float fill = getWidth() * getProgress() / (float) getMax();
            paint.setShader(track);
            canvas.drawRect(fill, 0, getWidth(), getHeight(), paint);
            if (getProgress() > 0) {
                paint.setShader(gradient);
                canvas.drawRect(0, 0, fill, getHeight(), paint);
                paint.setShader(crossLight);
                canvas.drawRect(0, 0, fill, getHeight(), paint);
            }
            canvas.restore();
            paint.setShader(null);
            paint.setStyle(Paint.Style.STROKE);
            paint.setStrokeWidth(getResources().getDisplayMetrics().density);
            paint.setColor(0xA8FFFFFF);
            canvas.drawPath(shape, paint);
            paint.setStyle(Paint.Style.FILL);
            paint.setColor(0xFFFFFFFF);
        }
    }

    private static final class VerticalSlider extends FrameLayout {
        private final SeekBar slider;

        VerticalSlider(android.content.Context context, SeekBar slider) {
            super(context);
            this.slider = slider;
            slider.setRotation(-90);
            addView(slider);
        }

        @Override protected void onMeasure(int widthSpec, int heightSpec) {
            int width = MeasureSpec.getSize(widthSpec);
            int height = MeasureSpec.getSize(heightSpec);
            setMeasuredDimension(width, height);
            slider.measure(MeasureSpec.makeMeasureSpec(height, MeasureSpec.EXACTLY),
                    MeasureSpec.makeMeasureSpec(width, MeasureSpec.EXACTLY));
        }

        @Override protected void onLayout(boolean changed, int left, int top, int right, int bottom) {
            int width = getWidth();
            int height = getHeight();
            int x = (width - height) / 2;
            int y = (height - width) / 2;
            slider.layout(x, y, x + height, y + width);
        }
    }

    private void updateConnection(int response, boolean success) {
        if (success)
            showConnection("PC 연결됨", 0xFFC5F9DB);
        else if (response == 503)
            showConnection("PC 제어 꺼짐", 0xFFFFE6AA);
        else if (response == 401)
            showConnection("다시 연결 필요", 0xFFFFE6AA);
        else if (response == -1)
            showConnection("PC 연결 안 됨", 0xFFFFDFE6);
    }

    private void applyReadableText(TextView view, int color) {
        view.setTextColor(color);
        view.setShadowLayer(dp(2), 0, 0, 0xFF142033);
    }

    private void showConnection(String label, int color) {
        if (("●  " + label).contentEquals(connectionStatus.getText()) && color == connectionStatus.getCurrentTextColor())
            return;
        connectionStatus.setText("●  " + label);
        applyReadableText(connectionStatus, color);
    }
}
