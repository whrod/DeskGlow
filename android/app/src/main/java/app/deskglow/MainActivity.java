package app.deskglow;

import androidx.activity.ComponentActivity;
import androidx.activity.result.ActivityResultLauncher;
import com.journeyapps.barcodescanner.ScanContract;
import com.journeyapps.barcodescanner.ScanOptions;
import android.os.Bundle;
import android.view.WindowManager;
import android.widget.*;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class MainActivity extends ComponentActivity {
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
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
    private TextView status;
    private TextView connectionStatus;
    private LinearLayout settings;
    private Button checkButton;
    private SeekBar volumeSlider;
    private TextView volumeValue;
    private Button muteButton;
    private boolean draggingVolume;
    private boolean volumePending;
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
        LinearLayout screen = new LinearLayout(this);
        screen.setOrientation(LinearLayout.VERTICAL);
        screen.setPadding(16, 8, 16, 8);
        LinearLayout header = new LinearLayout(this);
        header.setGravity(android.view.Gravity.CENTER_VERTICAL);
        screen.addView(header);
        connectionStatus = new TextView(this);
        connectionStatus.setTextSize(20);
        connectionStatus.setTypeface(null, android.graphics.Typeface.BOLD);
        header.addView(connectionStatus, new LinearLayout.LayoutParams(0, -2, 1));
        Button settingsButton = new Button(this);
        settingsButton.setText("연결 설정");
        header.addView(settingsButton);

        settings = new LinearLayout(this);
        screen.addView(settings);
        settingsButton.setOnClickListener(view -> settings.setVisibility(
                settings.getVisibility() == android.view.View.VISIBLE
                        ? android.view.View.GONE : android.view.View.VISIBLE));
        address = field(settings, "http://PC주소:8766", "address");
        address.setInputType(17);
        code = field(settings, "페어링 코드", "code");
        code.setInputType(129);
        checkButton = button(settings, "연결 확인", "check");
        Button scanButton = new Button(this);
        scanButton.setText("QR로 연결");
        settings.addView(scanButton);
        scanButton.setOnClickListener(view -> scanner.launch(new ScanOptions()
                .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                .setPrompt("Windows의 DeskGlow QR을 스캔하세요.")
                .setBeepEnabled(false)));
        status = new TextView(this);
        screen.addView(status);
        LinearLayout controls = new LinearLayout(this);
        screen.addView(controls, new LinearLayout.LayoutParams(-1, 0, 1));
        LinearLayout grid = new LinearLayout(this);
        grid.setOrientation(LinearLayout.VERTICAL);
        controls.addView(grid, new LinearLayout.LayoutParams(0, -1, 1));
        for (int row = 0; row < 2; row++) {
            LinearLayout line = new LinearLayout(this);
            grid.addView(line, new LinearLayout.LayoutParams(-1, 0, 1));
            for (int column = 0; column < 4; column++) {
                if (row == 0 && column == 0) button(line, "Google 열기", "site");
                else if (column == 0) button(line, "PC 절전", "sleep");
                else line.addView(new android.view.View(this),
                        new LinearLayout.LayoutParams(0, -1, 1));
            }
        }
        controls.addView(createVolumePanel(), new LinearLayout.LayoutParams(dp(112), -1));
        setContentView(screen);
        boolean paired = !address.getText().toString().isEmpty() && !code.getText().toString().isEmpty();
        settings.setVisibility(paired ? android.view.View.GONE : android.view.View.VISIBLE);
        if (paired) {
            showConnection("PC 확인 중", 0xFFD9B46B);
        } else {
            showConnection("PC 연결 필요", 0xFFB6C3D5);
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
        super.onStop();
    }

    @Override
    protected void onDestroy() {
        worker.shutdownNow();
        super.onDestroy();
    }

    private EditText field(LinearLayout row, String hint, String key) {
        EditText field = new EditText(this);
        field.setSingleLine(true);
        field.setHint(hint);
        field.setText(getPreferences(MODE_PRIVATE).getString(key, ""));
        row.addView(field, new LinearLayout.LayoutParams(0, -2, 1));
        return field;
    }

    private LinearLayout createVolumePanel() {
        LinearLayout panel = new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setGravity(android.view.Gravity.CENTER_HORIZONTAL);
        volumeValue = new TextView(this);
        volumeValue.setTextSize(22);
        panel.addView(volumeValue);
        volumeSlider = new SeekBar(this);
        volumeSlider.setMax(100);
        volumeSlider.setContentDescription("PC 볼륨");
        panel.addView(new VerticalSlider(this, volumeSlider),
                new LinearLayout.LayoutParams(dp(56), 0, 1));
        muteButton = new Button(this);
        muteButton.setText("음소거");
        panel.addView(muteButton);
        muteButton.setOnClickListener(view -> sendVolume("toggle-mute"));
        volumeSlider.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onStartTrackingTouch(SeekBar slider) { draggingVolume = true; }
            @Override public void onStopTrackingTouch(SeekBar slider) {
                draggingVolume = false;
                sendVolume("volume-" + slider.getProgress());
            }
            @Override public void onProgressChanged(SeekBar slider, int level, boolean fromUser) {
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
        muteButton.setText(available && muted ? "음소거 해제" : "음소거");
        if (available) volumeSlider.setProgress(Integer.parseInt(level));
        volumeValue.setText(available ? level + "%" : "—");
    }

    private Button button(LinearLayout row, String label, String action) {
        Button button = new Button(this);
        button.setText(label);
        row.addView(button, new LinearLayout.LayoutParams(0, -1, 1));
        button.setOnClickListener(view -> {
            if ("sleep".equals(action)) {
                new android.app.AlertDialog.Builder(this)
                        .setTitle("PC를 절전 모드로 전환할까요?")
                        .setMessage("다시 사용하려면 PC에 연결된 키보드의 키를 누르세요.")
                        .setNegativeButton("취소", null)
                        .setPositiveButton("절전", (dialog, which) -> send(action, button))
                        .show();
            } else {
                send(action, button);
            }
        });
        return button;
    }

    private void send(String action, Button button) {
        boolean backgroundCheck = button == null;
        boolean audioAction = action.startsWith("volume-") || "toggle-mute".equals(action);
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
                connected = result == 204 || ("sleep".equals(action) && result == 202);
                message = result == 202 && connected ? "PC에 절전 요청을 보냈습니다." :
                        connected ? ("check".equals(action) || audioAction ? "" : "명령 전송 완료") :
                        result == 404 && ("sleep".equals(action) || audioAction) ? "Windows DeskGlow를 업데이트하세요." :
                        result == 422 && audioAction ? "PC의 기본 오디오 출력 장치를 확인하세요." :
                        result == 503 ? "Windows에서 PC 제어가 꺼져 있습니다." :
                        result == 401 ? "Windows에 표시된 새 코드를 확인하세요." : "명령 실행 실패";
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
            runOnUiThread(() -> {
                if (isDestroyed()) return;
                if (backgroundCheck) {
                    if (host.equals(getPreferences(MODE_PRIVATE).getString("address", "")) &&
                            token.equals(getPreferences(MODE_PRIVATE).getString("code", ""))) {
                        updateConnection(response, success);
                        updateVolume(level, muted);
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
            });
        });
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
            showConnection("PC 연결됨", 0xFF79E5AE);
        else if (response == 503)
            showConnection("PC 제어 꺼짐", 0xFFD9B46B);
        else if (response == 401)
            showConnection("다시 연결 필요", 0xFFD9B46B);
        else if (response == -1)
            showConnection("PC 연결 안 됨", 0xFFFFA4A4);
    }

    private void showConnection(String label, int color) {
        connectionStatus.setText("●  " + label);
        connectionStatus.setTextColor(color);
    }
}
