package com.clinicmanagement.shell

import android.app.Activity
import android.content.ClipData
import android.content.Intent
import android.net.Uri
import android.provider.MediaStore
import android.util.Log
import android.webkit.MimeTypeMap
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.FileProvider
import java.io.File

/**
 * `<input type="file">`, which a WebView does **nothing** about on its own (AC-18).
 *
 * `WebChromeClient.onShowFileChooser` has no default implementation: without this class all six file inputs in the
 * app — the patient-files upload, the CSV import, the clinic logo, the practitioner's cachet, the document
 * attachment and the patient-file drop zone — open no picker and report no error. Tapping them does nothing at all,
 * which is § 0's "never fail silently" in its purest form.
 *
 * ⚠️ **Construct this in `onCreate`.** `registerForActivityResult` throws if it is called once the activity has
 * reached STARTED.
 */
class FileChooser(private val activity: ComponentActivity) {

    private var pendingCallback: ValueCallback<Array<Uri>>? = null
    private var pendingCameraOutput: Uri? = null

    private val launcher = activity.registerForActivityResult(
        ActivityResultContracts.StartActivityForResult(),
    ) { result ->
        val callback = pendingCallback
        pendingCallback = null
        val cameraOutput = pendingCameraOutput
        pendingCameraOutput = null

        if (callback == null) return@registerForActivityResult

        if (result.resultCode != Activity.RESULT_OK) {
            // ⚠️ Cancelling MUST answer with null. A WebView whose chooser callback is never invoked leaves the
            // input element in a permanently pending state — every later tap on it is ignored, so a cancelled
            // pick would break the control for the rest of the session.
            callback.onReceiveValue(null)
            return@registerForActivityResult
        }

        val data = result.data
        val fromPicker = WebChromeClient.FileChooserParams.parseResult(result.resultCode, data)
        val uris = when {
            fromPicker != null && fromPicker.isNotEmpty() -> fromPicker
            // A camera capture returns no Intent data at all — the photo is at the Uri we handed it, and only
            // if the file actually has bytes (some camera apps report OK after writing nothing).
            cameraOutput != null && hasContent(cameraOutput) -> arrayOf(cameraOutput)
            else -> null
        }
        callback.onReceiveValue(uris)
    }

    /** Returns `true` when the chooser was launched, i.e. when the WebView must not fall back to doing nothing. */
    fun show(
        callback: ValueCallback<Array<Uri>>,
        params: WebChromeClient.FileChooserParams,
    ): Boolean {
        // A second chooser while one is open would orphan the first callback, which is the same permanently-dead
        // input as never answering it.
        pendingCallback?.onReceiveValue(null)
        pendingCallback = callback
        pendingCameraOutput = null

        val mimeTypes = resolveMimeTypes(params.acceptTypes)
        val allowMultiple = params.mode == WebChromeClient.FileChooserParams.MODE_OPEN_MULTIPLE

        val content = Intent(Intent.ACTION_GET_CONTENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = if (mimeTypes.size == 1) mimeTypes.first() else ANY_TYPE
            if (mimeTypes.size > 1) putExtra(Intent.EXTRA_MIME_TYPES, mimeTypes)
            putExtra(Intent.EXTRA_ALLOW_MULTIPLE, allowMultiple)
        }

        val chooser = Intent.createChooser(content, activity.getString(R.string.file_chooser_title))

        // AC-18's second half: an image input offers the camera. A dentist photographing a radiograph or a
        // referral letter is the reason the patient-files input exists at all, and « choisir un fichier » alone
        // would send them to the gallery to find a photo they have not taken yet.
        if (mimeTypes.any { it.startsWith("image/") } || mimeTypes.contains(ANY_TYPE)) {
            cameraIntent()?.let { chooser.putExtra(Intent.EXTRA_INITIAL_INTENTS, arrayOf(it)) }
        }

        return try {
            launcher.launch(chooser)
            true
        } catch (t: Throwable) {
            Log.w(TAG, "file chooser could not be launched", t)
            pendingCallback = null
            pendingCameraOutput = null
            callback.onReceiveValue(null)
            Toast.makeText(activity, R.string.file_chooser_camera_failed, Toast.LENGTH_LONG).show()
            false
        }
    }

    private fun cameraIntent(): Intent? {
        val intent = Intent(MediaStore.ACTION_IMAGE_CAPTURE)
        // Needs the `<queries>` element in the manifest to resolve at all on API 30+.
        if (intent.resolveActivity(activity.packageManager) == null) return null

        return try {
            val directory = File(activity.cacheDir, ShellBridge.SHARED_DIRECTORY).apply { mkdirs() }
            val target = File(directory, "capture-${System.currentTimeMillis()}.jpg")
            val uri = FileProvider.getUriForFile(activity, "${BuildConfig.APPLICATION_ID}.files", target)
            pendingCameraOutput = uri
            intent.apply {
                putExtra(MediaStore.EXTRA_OUTPUT, uri)
                // Flags alone do not grant write access to an EXTRA_OUTPUT Uri on every OEM camera app; setting
                // clipData as well is the long-standing workaround, and without it the capture silently writes
                // nothing and the picker returns an empty file.
                clipData = ClipData.newRawUri("", uri)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
            }
        } catch (t: Throwable) {
            Log.w(TAG, "camera capture target could not be prepared", t)
            pendingCameraOutput = null
            null
        }
    }

    private fun hasContent(uri: Uri): Boolean = try {
        activity.contentResolver.openInputStream(uri)?.use { it.read() != -1 } ?: false
    } catch (t: Throwable) {
        Log.w(TAG, "camera output unreadable", t)
        false
    }

    /**
     * `accept` as the platform needs it.
     *
     * ⚠️ An `accept` attribute may hold **extensions** (`.csv`) as well as MIME types, and the CSV import's does.
     * Handing `".csv"` to the picker as a `type` matches nothing, so the file list comes up empty and the import
     * looks broken rather than unsupported. Extensions are resolved through `MimeTypeMap`, and anything still
     * unresolvable widens to [ANY_TYPE] — showing every file is a worse filter but a working one.
     */
    private fun resolveMimeTypes(acceptTypes: Array<String>?): Array<String> {
        val resolved = acceptTypes.orEmpty()
            .flatMap { it.split(',') }
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .mapNotNull { entry ->
                when {
                    entry.startsWith(".") ->
                        MimeTypeMap.getSingleton().getMimeTypeFromExtension(entry.removePrefix(".").lowercase())
                    entry.contains('/') -> entry
                    else -> null
                }
            }
            .distinct()

        return if (resolved.isEmpty()) arrayOf(ANY_TYPE) else resolved.toTypedArray()
    }

    private companion object {
        const val TAG = "ClinicShell"
        const val ANY_TYPE = "*/*"
    }
}
