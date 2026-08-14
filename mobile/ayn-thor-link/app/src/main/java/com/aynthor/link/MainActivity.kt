package com.aynthor.link

import android.content.Intent
import android.net.wifi.WifiManager
import android.os.Bundle
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity

class MainActivity : AppCompatActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        // Start the background service
        startForegroundService(Intent(this, LinkService::class.java))

        // Show IP info
        val ip = getWifiIp()
        findViewById<TextView>(R.id.tvStatus).text = "Ativo"
        findViewById<TextView>(R.id.tvIp).text = "IP: $ip\nPorta: ${LinkService.PORT}"
        findViewById<TextView>(R.id.tvInfo).text =
            "O PC encontra este dispositivo automaticamente.\n\n" +
            "Se for a primeira vez, rode no PC (via USB):\n" +
            "adb shell pm grant com.aynthor.link android.permission.WRITE_SECURE_SETTINGS"
    }

    private fun getWifiIp(): String {
        val wm = applicationContext.getSystemService(WIFI_SERVICE) as WifiManager
        val ip = wm.connectionInfo.ipAddress
        return "${ip and 0xff}.${ip shr 8 and 0xff}.${ip shr 16 and 0xff}.${ip shr 24 and 0xff}"
    }
}
