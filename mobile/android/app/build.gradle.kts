import org.jetbrains.kotlin.gradle.dsl.JvmTarget
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

        // The address a fresh install starts on, so a phone that downloads this build from the product's own
        // download page connects with nothing typed — the friction the iOS route does not have, because there the
        // user *arrives* at the server by opening its URL.
        //
        // ⚠️ **A starting value, not a compiled-in server.** `ServerConfigStore` consults it only when nothing is
        // stored, « Serveur → Changer de serveur… » still reaches every address, and a chosen address is persisted
        // and wins for ever after. Empty — the default, and what `gradle.properties` leaves it as — reproduces the
        // original behaviour exactly: the address screen on first launch. So the invariant the shell is built on
        // still holds, and it is worth restating precisely because this line looks like it breaks it: *one build
        // still serves a clinic's own PC on a LAN and a hosted backend on the internet.* What is new is only that
        // a build published for one of them may be **aimed** at it.
        //
        // Set it per build rather than committing a value: an address in `gradle.properties` is an address that
        // rots in the repository, and the deployment a given APK is published for is a property of the publish,
        // not of the source. See `mobile/README.md` § « Building the APK for the download page ».
        //
        //   ./gradlew assembleRelease -PclinicServerAddress=clinic.example.com
        buildConfigField(
            "String",
            "DEFAULT_SERVER_ADDRESS",
            "\"${providers.gradleProperty("clinicServerAddress").getOrElse("").trim()}\"",
        )
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

    buildFeatures {
        buildConfig = true
    }

    lint {
        // Android Lint *is* this module's gate — there is no test runner here, so it holds the repo's 0-warnings
        // policy the way `check:responsive` + `tsc` do for `web/`. Proved by clearing all 16 warnings the first
        // run reported rather than by lowering the bar. Since `.github/workflows/ci.yml` landed it runs on every
        // push, which is what makes the two `disable` decisions below load-bearing rather than local taste.
        warningsAsErrors = true
        abortOnError = true
        // ⚠️ **`OldTargetApi` — the second check disabled, and disabled for the reason the block below already
        // states in full.** It fires on `targetSdk = 36` with « Not targeting the latest versions of Android »,
        // i.e. it reddens the moment Google ships an API level — on **somebody else's release schedule**, with
        // nothing in this project having changed. It did exactly that: CI went red on a commit that touched only
        // `deploy/` and the backend, while `./gradlew lintDebug --rerun-tasks` on a developer machine (SDK 35 + 36
        // installed, same pinned AGP) stayed green, because the hosted runner image had learned about a newer
        // platform than this one has.
        //
        // ⚠️ **It is not disabled to dodge the advice.** Raising `targetSdk` changes runtime behaviour for every
        // user — that is the point of the field — so it is a deliberate change that has to be walked on a real
        // device, exactly like the four dependency versions below. What must not happen is that decision arriving
        // as a broken build on an unrelated commit, because the only move available at that moment is the wrong
        // one: bump the number to get green, having tested nothing.
        disable += "OldTargetApi"
        // ⚠️ The first of the two, and it is not a defect report either: it says a newer AndroidX exists.
        //
        // Its original reason has **expired** — it read « bumping any of the four would require compileSdk 36,
        // which in turn requires a newer AGP », and compileSdk *is* 36 now. It stays disabled for a different and
        // better reason: under `warningsAsErrors` this is the only check here that reddens with the **passage of
        // time** rather than with a change to this project. A build that breaks because a library shipped a release
        // overnight, on a module whose only gate this is, teaches the operator to reach for `--continue` rather
        // than to read the failure — and that is how the other checks lose their authority too.
        //
        // The four versions below therefore move only as a deliberate, separately-verified change. They are not
        // bumped as a side effect of an SDK bump, because a dependency upgrade that rides along in another commit
        // is one nobody reviewed as an upgrade.
        disable += "GradleDependency"
    }
}

// `kotlinOptions { jvmTarget }` inside `android { }` is deprecated as of the Kotlin 2.2 plugin, which the
// Gradle 8.13 / AGP 8.13 bump brought in — it warned on every compile, and this module runs Android Lint with
// `warningsAsErrors` precisely because it has no test runner and no CI, so a tolerated warning is how the rest
// stop being read. This is the replacement DSL, and it must be a top-level `kotlin { }` block rather than a
// member of `android { }`.
kotlin {
    compilerOptions {
        jvmTarget.set(JvmTarget.JVM_17)
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
