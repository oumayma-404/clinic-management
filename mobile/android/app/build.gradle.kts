import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

/**
 * The release signing material, read from a **git-ignored** `keystore.properties` beside `settings.gradle.kts`.
 *
 * ⚠️ **Absent is a first-class case, not an error.** A machine without the keystore still builds `assembleRelease`
 * — unsigned — because the release build type is also how R8 and `isShrinkResources` get exercised, and only the
 * publishing machine has any business holding the upload key. Failing the build here would mean nobody but the
 * publisher could ever test a minified build, which is the one build the store receives.
 *
 * The alternative shape — `-Pandroid.injected.signing.*` on the command line, which `mobile/README.md` used to
 * document — works without this block, so the old claim that the module « can only produce debug-signed builds »
 * was never true. It is not used because a password passed as a Gradle property lands in shell history and in the
 * process list, it has to be retyped correctly on every publish, and it cannot be reproduced by a CI runner
 * reading secrets from the environment.
 */
val keystoreProperties: Properties? = rootProject.file("keystore.properties")
    .takeIf { it.exists() }
    ?.let { file -> Properties().apply { file.inputStream().use(::load) } }

android {
    namespace = "com.clinicmanagement.shell"

    // 36 because Google Play refuses a new submission below it from 31 August 2026. See the version-chain note in
    // the root `build.gradle.kts`: this line cannot move on its own.
    compileSdk = 36

    defaultConfig {
        // SETTLED. It must stay in step with iOS's `PRODUCT_BUNDLE_IDENTIFIER`, and an applicationId cannot be
        // changed after the first store submission — see `mobile/README.md` § « Avant la première soumission ».
        applicationId = "com.clinicmanagement.shell"
        minSdk = 26
        targetSdk = 36

        // ⚠️ Must increase on every upload Play accepts, and it can never go back down for this `applicationId`.
        // 1 and 2 were pre-store builds that never left this machine; 3 is the first that may.
        versionCode = 3

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

    signingConfigs {
        keystoreProperties?.let { props ->
            create("release") {
                // `rootProject.file`, not `file`: this block lives in `app/`, so a bare `file()` would resolve a
                // relative path against `app/` while `keystore.properties` sits one level up beside
                // `settings.gradle.kts`. Absolute paths pass through either way — and an upload key belongs
                // outside the repository, so both forms have to work.
                storeFile = rootProject.file(props.getProperty("storeFile"))
                storePassword = props.getProperty("storePassword")
                keyAlias = props.getProperty("keyAlias")
                keyPassword = props.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            // `findByName` rather than `getByName`: null is the no-keystore machine, and an unsigned release APK is
            // still a useful artifact (R8, resource shrinking, the `@JavascriptInterface` keep rule). Only a signed
            // one can be installed on a phone or uploaded to Play.
            signingConfig = signingConfigs.findByName("release")
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
        //
        // Its original reason has **expired** — it read « bumping any of the four would require compileSdk 36,
        // which in turn requires a newer AGP », and compileSdk *is* 36 now. It stays disabled for a different and
        // better reason: under `warningsAsErrors` this is the only check here that reddens with the **passage of
        // time** rather than with a change to this project. A build that breaks because a library shipped a release
        // overnight, on a module with no CI and no test runner, teaches the operator to reach for `--continue`
        // rather than to read the failure — and that is how the other checks lose their authority too.
        //
        // The four versions below therefore move only as a deliberate, separately-verified change. They are not
        // bumped as a side effect of an SDK bump, because a dependency upgrade that rides along in another commit
        // is one nobody reviewed as an upgrade.
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
