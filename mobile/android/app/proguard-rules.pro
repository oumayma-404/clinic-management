# The bridge is reached by name from JavaScript, so R8 must not rename or strip it — a minified release would
# leave `window.__clinicShell.saveFile` calling a method that no longer exists, and only in release.
-keepclassmembers class com.clinicmanagement.shell.ShellBridge {
    @android.webkit.JavascriptInterface <methods>;
}
