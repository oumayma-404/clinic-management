// Versions are pinned, not ranged: this build is operator-run on one machine at a time with no CI to catch a
// resolution that drifted overnight. AGP 8.13 / Gradle 8.13 / Kotlin 2.2.20 is the pairing verified against the
// installed SDK (platforms;android-36 + build-tools;36.0.0) — see `mobile/README.md`.
//
// ⚠️ **The four versions move as one set, and the chain starts at Google Play, not at us.** New submissions must
// target API 36 from 31 August 2026, and `compileSdk = 36` needs **AGP ≥ 8.9.1**; AGP 8.13.0 in turn *requires*
// Gradle 8.13; and Gradle above 8.10 is outside Kotlin 2.0.21's supported range. So « bump targetSdk » is never a
// one-line change here — raising any one of these four without the others fails in a way that names the wrong
// culprit. The previous set (AGP 8.7.3 / Gradle 8.9 / Kotlin 2.0.21 / SDK 35) is what this replaced.
plugins {
    id("com.android.application") version "8.13.0" apply false
    id("org.jetbrains.kotlin.android") version "2.2.20" apply false
}
