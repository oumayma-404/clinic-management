plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.clinicmanagement.shell"
    compileSdk = 35

    defaultConfig {
        // ⚠️ PROVISIONAL. The bundle identifier and the display name are two of Part 8's four deferred business
        // decisions, and an applicationId cannot be changed after the first store submission. Settle it before
        // uploading anything — see `mobile/README.md` § « Avant la première soumission ».
        applicationId = "com.clinicmanagement.shell"
        minSdk = 26
        targetSdk = 35
        versionCode = 2

        // The single source of the shell's version. `BuildConfig.VERSION_NAME` is what reaches
        // `window.__clinicShell.version` and therefore `X-Client-Version`, so the build and the bridge cannot
        // report different builds (AC-27). `System.Version.TryParse` on the server must accept it: keep it
        // numeric and dotted.
        //
        // ⚠️ A change to the bridge's method set edits `mobile/shared/bridge.md` **and** bumps this — one without
        // the other ships a build reporting a capability set it does not have. 1.1.0 added `confirmIdentity`
        // (Part 7); its version history is the table at the foot of that file.
        versionName = "1.1.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        buildConfig = true
    }

    lint {
        // Android Lint *is* this module's gate — there is no test runner here and no CI, so it holds the repo's
        // 0-warnings policy the way `check:responsive` + `tsc` do for `web/`. Proved by clearing all 16 warnings
        // the first run reported rather than by lowering the bar.
        warningsAsErrors = true
        abortOnError = true
        // ⚠️ The one check that is disabled, and it is not a defect report: it says a newer AndroidX exists.
        // Bumping any of the four would require compileSdk 36, which in turn requires a newer AGP than the one
        // pinned above — so the honest state is « pinned to the SDK this project is verified against », which
        // `mobile/README.md` states as an operator instruction. Re-enable it the day the SDK moves.
        disable += "GradleDependency"
    }
}

dependencies {
    // Four, and each earns its place: `core-ktx` for FileProvider + WindowInsetsCompat, `activity` for
    // ComponentActivity's result contracts and back dispatcher, `browser` for Custom Tabs (AC-25), `webkit` for
    // `addDocumentStartJavaScript` — the only API that can install the bridge before the page's own scripts run.
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.activity:activity-ktx:1.9.3")
    implementation("androidx.browser:browser:1.8.0")
    implementation("androidx.webkit:webkit:1.12.1")
}
