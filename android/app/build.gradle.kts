plugins {
    alias(libs.plugins.android.application)
}

kotlin {
    jvmToolchain(21)
}

android {
    namespace = "aria.monitor"
    compileSdk = 36

    defaultConfig {
        applicationId = "aria.monitor"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0"
    }
}

dependencies {
    implementation(project(":core"))
    implementation(project(":engine"))
}
