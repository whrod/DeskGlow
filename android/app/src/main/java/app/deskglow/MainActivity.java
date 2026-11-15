package app.deskglow;

import androidx.activity.ComponentActivity;
import androidx.activity.result.ActivityResultLauncher;
import com.journeyapps.barcodescanner.ScanContract;
import com.journeyapps.barcodescanner.ScanOptions;
import android.os.Bundle;
import android.content.Intent;
import android.content.IntentFilter;
import android.os.BatteryManager;
import android.view.WindowManager;
import android.widget.*;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class MainActivity extends ComponentActivity {
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private EditText address;
    private EditText code;
    private TextView status;
    private Button checkButton;
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
        LinearLayout settings = new LinearLayout(this);
        screen.addView(settings);
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
        status.setText("Windows 프로그램에 표시된 주소와 코드를 입력하세요.");
        screen.addView(status);
        LinearLayout grid = new LinearLayout(this);
        grid.setOrientation(LinearLayout.VERTICAL);
        screen.addView(grid, new LinearLayout.LayoutParams(-1, 0, 1));
        String[] labels = {"볼륨 −", "볼륨 +", "음소거", "Google 열기"};
        String[] actions = {"down", "up", "mute", "site"};
        for (int row = 0; row < 2; row++) {
            LinearLayout line = new LinearLayout(this);
            grid.addView(line, new LinearLayout.LayoutParams(-1, 0, 1));
            for (int column = 0; column < 4; column++) {
                if (row == 0) button(line, labels[column], actions[column]);
                else line.addView(new android.view.View(this),
                        new LinearLayout.LayoutParams(0, -1, 1));
            }
        }
        setContentView(screen);
        if (!address.getText().toString().isEmpty() && !code.getText().toString().isEmpty())
            send("check", checkButton);
    }

    private final android.content.BroadcastReceiver power = new android.content.BroadcastReceiver() {
        @Override
        public void onReceive(android.content.Context context, Intent intent) {
            if (intent.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) != 0)
                getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
            else
                getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        }
    };

    @Override
    protected void onStart() {
        super.onStart();
        registerReceiver(power, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
    }

    @Override
    protected void onStop() {
        unregisterReceiver(power);
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

    private Button button(LinearLayout row, String label, String action) {
        Button button = new Button(this);
        button.setText(label);
        row.addView(button, new LinearLayout.LayoutParams(0, -1, 1));
        button.setOnClickListener(view -> send(action, button));
        return button;
    }

    private void send(String action, Button button) {
        String host = address.getText().toString().trim();
        String token = code.getText().toString().trim();
        if (!host.matches("http://[0-9.]+:8766/?") || !token.matches("[0-9A-Fa-f]{24}")) {
            status.setText("PC 주소와 24자리 페어링 코드를 확인하세요.");
            return;
        }
        button.setEnabled(false);
        status.setText("전송 중");
        worker.execute(() -> {
            String message;
            HttpURLConnection connection = null;
            boolean connected = false;
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
                connected = result == 204;
                message = connected ? ("check".equals(action) ? "PC 연결됨" : "명령 전송 완료") :
                        result == 401 ? "Windows에 표시된 새 코드를 확인하세요." : "명령 실행 실패";
            } catch (java.io.IOException error) {
                message = "PC 주소, Wi-Fi 및 Windows 방화벽을 확인하세요.";
            } finally {
                if (connection != null) connection.disconnect();
            }
            String feedback = message;
            boolean success = connected;
            runOnUiThread(() -> {
                if (isDestroyed()) return;
                if (success) getPreferences(MODE_PRIVATE).edit()
                        .putString("address", host).putString("code", token).apply();
                status.setText(feedback);
                button.setEnabled(true);
            });
        });
    }
}
