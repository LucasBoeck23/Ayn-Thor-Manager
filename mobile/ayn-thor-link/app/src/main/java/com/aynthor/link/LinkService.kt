package com.aynthor.link

import android.app.*
import android.content.Context
import android.content.Intent
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.os.IBinder
import android.provider.Settings
import android.util.Log
import java.io.*
import java.net.ServerSocket
import java.net.Socket

/**
 * Background service that:
 * 1. Keeps ADB wireless debug enabled
 * 2. Announces via mDNS for auto-discovery
 * 3. Runs a TCP command server for file operations (port 7100)
 */
class LinkService : Service() {

    private var serverSocket: ServerSocket? = null
    private var nsdManager: NsdManager? = null
    private var isRunning = false

    companion object {
        const val PORT = 7100
        const val SERVICE_TYPE = "_aynthor._tcp."
        const val TAG = "AynThorLink"
        const val NOTIFICATION_ID = 1
        const val CHANNEL_ID = "ayn_thor_link"
    }

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        startForeground(NOTIFICATION_ID, buildNotification("Iniciando..."))
        enableAdbWireless()
        startServer()
        registerMdns()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        return START_STICKY // Restart if killed
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        isRunning = false
        serverSocket?.close()
        unregisterMdns()
        super.onDestroy()
    }

    // === ADB Wireless Activation ===
    private fun enableAdbWireless() {
        try {
            // Requires WRITE_SECURE_SETTINGS permission (granted via adb once)
            Settings.Global.putInt(contentResolver, "adb_wifi_enabled", 1)
            Log.i(TAG, "ADB wireless enabled")
        } catch (e: SecurityException) {
            Log.w(TAG, "Cannot enable ADB wireless: ${e.message}")
            Log.w(TAG, "Run: adb shell pm grant com.aynthor.link android.permission.WRITE_SECURE_SETTINGS")
        }
    }

    // === TCP Command Server ===
    private fun startServer() {
        isRunning = true
        Thread {
            try {
                serverSocket = ServerSocket(PORT)
                Log.i(TAG, "Server listening on port $PORT")
                updateNotification("Ativo — porta $PORT")

                while (isRunning) {
                    val client = serverSocket?.accept() ?: break
                    Thread { handleClient(client) }.start()
                }
            } catch (e: Exception) {
                if (isRunning) Log.e(TAG, "Server error: ${e.message}")
            }
        }.start()
    }

    private fun handleClient(socket: Socket) {
        try {
            val reader = BufferedReader(InputStreamReader(socket.getInputStream()))
            val writer = PrintWriter(socket.getOutputStream(), true)

            writer.println("AYNTHOR_LINK v1.0")

            var line: String?
            while (reader.readLine().also { line = it } != null) {
                val response = processCommand(line!!)
                writer.println(response)
                if (line == "QUIT") break
            }

            socket.close()
        } catch (e: Exception) {
            Log.d(TAG, "Client disconnected: ${e.message}")
        }
    }

    private fun processCommand(command: String): String {
        val parts = command.split(" ", limit = 2)
        val cmd = parts[0].uppercase()
        val arg = parts.getOrElse(1) { "" }

        return when (cmd) {
            "PING" -> "PONG"
            "INFO" -> getDeviceInfo()
            "LS" -> listDirectory(arg)
            "MKDIR" -> makeDirectory(arg)
            "DELETE" -> deleteItem(arg)
            "QUIT" -> "BYE"
            else -> "ERR unknown command: $cmd"
        }
    }

    private fun getDeviceInfo(): String {
        val model = android.os.Build.MODEL
        val brand = android.os.Build.BRAND
        val version = android.os.Build.VERSION.RELEASE
        val battery = getBatteryLevel()
        return "OK $brand $model|Android $version|Battery $battery%"
    }

    private fun getBatteryLevel(): Int {
        val bm = getSystemService(Context.BATTERY_SERVICE) as android.os.BatteryManager
        return bm.getIntProperty(android.os.BatteryManager.BATTERY_PROPERTY_CAPACITY)
    }

    private fun listDirectory(path: String): String {
        val dir = File(if (path.isEmpty()) "/storage/emulated/0" else path)
        if (!dir.exists()) return "ERR path not found: $path"
        if (!dir.isDirectory) return "ERR not a directory: $path"

        val entries = dir.listFiles()?.map { f ->
            val type = if (f.isDirectory) "D" else "F"
            val size = if (f.isFile) f.length() else 0
            "$type|${f.name}|$size|${f.lastModified()}"
        } ?: emptyList()

        return "OK ${entries.size}\n${entries.joinToString("\n")}"
    }

    private fun makeDirectory(path: String): String {
        val dir = File(path)
        return if (dir.mkdirs()) "OK $path" else "ERR cannot create: $path"
    }

    private fun deleteItem(path: String): String {
        val file = File(path)
        if (!file.exists()) return "ERR not found: $path"
        return if (file.deleteRecursively()) "OK deleted: $path" else "ERR cannot delete: $path"
    }

    // === mDNS Registration ===
    private fun registerMdns() {
        nsdManager = getSystemService(Context.NSD_SERVICE) as NsdManager

        val serviceInfo = NsdServiceInfo().apply {
            serviceName = "AynThor-${android.os.Build.MODEL.replace(" ", "")}"
            serviceType = SERVICE_TYPE
            port = PORT
        }

        nsdManager?.registerService(serviceInfo, NsdManager.PROTOCOL_DNS_SD, object : NsdManager.RegistrationListener {
            override fun onServiceRegistered(info: NsdServiceInfo) {
                Log.i(TAG, "mDNS registered: ${info.serviceName}")
            }
            override fun onRegistrationFailed(info: NsdServiceInfo, code: Int) {
                Log.w(TAG, "mDNS registration failed: $code")
            }
            override fun onServiceUnregistered(info: NsdServiceInfo) {}
            override fun onUnregistrationFailed(info: NsdServiceInfo, code: Int) {}
        })
    }

    private fun unregisterMdns() {
        try { nsdManager?.unregisterService(object : NsdManager.RegistrationListener {
            override fun onServiceRegistered(info: NsdServiceInfo) {}
            override fun onRegistrationFailed(info: NsdServiceInfo, code: Int) {}
            override fun onServiceUnregistered(info: NsdServiceInfo) {}
            override fun onUnregistrationFailed(info: NsdServiceInfo, code: Int) {}
        }) } catch (_: Exception) {}
    }

    // === Notification ===
    private fun createNotificationChannel() {
        val channel = NotificationChannel(CHANNEL_ID, "Ayn Thor Link", NotificationManager.IMPORTANCE_LOW)
        (getSystemService(NOTIFICATION_SERVICE) as NotificationManager).createNotificationChannel(channel)
    }

    private fun buildNotification(text: String): Notification {
        val intent = Intent(this, MainActivity::class.java)
        val pending = PendingIntent.getActivity(this, 0, intent, PendingIntent.FLAG_IMMUTABLE)

        return Notification.Builder(this, CHANNEL_ID)
            .setContentTitle("Ayn Thor Link")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setContentIntent(pending)
            .setOngoing(true)
            .build()
    }

    private fun updateNotification(text: String) {
        val nm = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
        nm.notify(NOTIFICATION_ID, buildNotification(text))
    }
}
