// Versions are pinned, not ranged: this build is operator-run on one machine at a time with no CI to catch a
// resolution that drifted overnight. AGP 8.7 / Gradle 8.9 / Kotlin 2.0.21 is the pairing verified against the
// installed SDK (platforms;android-35 + build-tools;35.0.0) — see `mobile/README.md`.
plugins {
    id("com.android.application") version "8.7.3" apply false
    id("org.jetbrains.kotlin.android") version "2.0.21" apply false
}
