// The Android shell is its own Gradle build, in neither `api/ClinicManagement.sln` nor `web/` — the same
// separation `desktop/` keeps, and for the same reason: a different toolchain must not be able to redden the
// backend or frontend gate.
pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "ClinicShell"
include(":app")
