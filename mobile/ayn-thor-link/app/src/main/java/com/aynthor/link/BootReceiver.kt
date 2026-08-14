package com.aynthor.link

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/**
 * Starts LinkService automatically when the device boots.
 */
class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_BOOT_COMPLETED) {
            context.startForegroundService(Intent(context, LinkService::class.java))
        }
    }
}
