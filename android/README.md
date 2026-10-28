# ARIA Monitor (Android)

Нативный клиент монитора музыканта: играет подложку и клик в такт залу по протоколу `/monitor` при заблокированном экране. Хост (десктопное ARIA) не меняется. Проект `android/` лежит вне `ARIA.slnx`.

Архитектура: `core` (чистый Kotlin/JVM: протокол, часы, математика таймлайна, сессия), `engine` (C++17 + Oboe: рендер в аудио-колбэке, PCM-пул, JNI), `app` (foreground-сервис, OkHttp, Compose).

## Что нужно

- JDK 21 (`brew install openjdk@21`).
- Android SDK в `~/Library/Android/sdk`: платформы 36 и 37, build-tools 36.0.0, NDK `28.2.13676358`, CMake `3.22.1`.
- Окружение подключается одной командой: `. scripts/android-env.sh` (задаёт `JAVA_HOME`, `ANDROID_HOME`, `CMAKE`, `PATH`).

## Релизный APK (как собирать правильно)

```sh
sh scripts/android-publish.sh
```

Результат: `publish/android/ARIA-Monitor-<версия>.apk`, подписанный. Скрипт, как `scripts/publish.sh`, делает всё сам:

1. подключает окружение (`scripts/android-env.sh`);
2. при первом запуске создаёт ключ подписи в `android/signing/` (см. ниже);
3. собирает `:app:assembleRelease` (аргументы скрипта передаются в Gradle, например `--offline`);
4. проверяет подпись (`apksigner`, схема v2) и выравнивание (`zipalign`, 16 КБ) и останавливается с ошибкой, если что-то не так;
5. кладёт APK в `publish/android/` (каталог в `.gitignore`) и печатает SHA-256 файла и отпечаток сертификата.

Установка на телефон: `adb install -r publish/android/ARIA-Monitor-<версия>.apk` или перенести файл и разрешить установку из неизвестных источников. Первый запуск ведёт через онбординг (уведомления, исключение из оптимизации батареи, QR или ручной ввод пары).

Версия берётся из `android/app/build.gradle.kts` (`versionName`, `versionCode`). Для нового релиза поднимите оба значения: Android не даст поставить APK с тем же или меньшим `versionCode` поверх установленного.

### Ключ подписи

Лежит в `android/signing/` (каталог в `.gitignore`, в репозиторий не попадает):

| файл | что |
|---|---|
| `aria-release.jks` | хранилище ключей, PKCS12, RSA 4096, срок 30 лет, алиас `aria-monitor` |
| `keystore.properties` | пути и пароли (`storeFile` относительный, от этого каталога) |

- Ключ создаётся один раз и **должен быть сохранён отдельно** (менеджер паролей, зашифрованный архив). Потерянный ключ означает, что новую версию нельзя поставить поверх старой: только удалить приложение и поставить заново, потеряв настройки и пару.
- Ключ в git не коммитится сознательно: репозиторий уходит на GitHub, а закрытый ключ подписи не должен там оказаться. Если нужно хранить его в репозитории, это ваше решение: уберите строку `android/signing/` из `.gitignore` и учтите последствия.
- Свой существующий ключ: положите `aria-release.jks` и `keystore.properties` (с тем же форматом) в `android/signing/` до первого запуска скрипта. Скрипт ничего не перезапишет и не создаст второй ключ. Если есть только один из двух файлов, он остановится с ошибкой.
- Другой каталог: переменная `ARIA_SIGNING_DIR` (скрипт) или `ARIA_KEYSTORE_PROPERTIES` (путь к `keystore.properties` для Gradle напрямую).
- Без ключа Gradle собирает неподписанный release (`assembleRelease` работает, но поставить такой APK нельзя). Debug-сборка подписывается отладочным ключом Android Studio и для установки на телефон годится.

Сверить подпись вручную:

```sh
~/Library/Android/sdk/build-tools/36.0.0/apksigner verify --print-certs publish/android/ARIA-Monitor-<версия>.apk
```

Отпечаток SHA-256 должен совпадать с `keytool -list -keystore android/signing/aria-release.jks`.

## Debug-сборка и тесты

```sh
. scripts/android-env.sh
cd android
./gradlew :app:assembleDebug                  # android/app/build/outputs/apk/debug/app-debug.apk
./gradlew :core:test :app:testDebugUnitTest   # JVM-тесты клиента
./gradlew :app:lintDebug                      # lint, ошибок быть не должно
```

Движок (C++), хост-тесты на GoogleTest; нативные тестовые бинарники собирать **только в каталог с `bin/` в пути**, иначе песочница убивает их (SIGKILL):

```sh
. scripts/android-env.sh
"$CMAKE" -S android/engine -B android/engine/bin/host -DARIA_HOST_TESTS=ON
"$CMAKE" --build android/engine/bin/host
ctest --test-dir android/engine/bin/host --output-on-failure
```

То же под ThreadSanitizer (обязательно после изменений в `pcm_pool`, `plan_slot`, рендерере): сборка в `android/engine/bin/tsan` с `-DARIA_SANITIZE_THREAD=ON`. После правок удаляйте старые `.o`: `make` на macOS сравнивает время изменения с точностью до секунды.

Паритет с вебом: `sh scripts/test-js.sh` (общие JSON-векторы в `tests/fixtures/parity`, контракт хоста в `tests/fixtures/monitor`, бинарный формат плана в `tests/fixtures/engine`).

Инструментальные тесты (нужен эмулятор или телефон, звук на эмуляторе недоступен):

```sh
cd android
./gradlew :engine:connectedDebugAndroidTest :app:connectedDebugAndroidTest
```

Эмулятор: образ `android-37.1`; запускать без окна (`-no-window -no-audio`), дожидаться `sys.boot_completed`, после тестов обязательно завершать `emulator` и `adb`.

Перед коммитом: всё выше зелёное, ноль предупреждений (`allWarningsAsErrors`, `-Werror`), в коде нет комментариев (правило `AGENTS.md`).

## Подводные камни

- Команды `timeout` на macOS нет; не оборачивайте в неё Gradle, ошибка потеряется.
- `./gradlew --stop` не запускайте, если параллельно идёт чужая сборка: демоны общие.
- Первая сборка скачивает зависимости; повторные идут с `--offline`. Артефакты `androidx.test`, CameraX и ML Kit нужны онлайн один раз.
- compileSdk у `:app` 37 (требует OkHttp 5.5.0), у `:engine` 36; `minSdk` 26, `targetSdk` 36.
- В APK только ABI `arm64-v8a`, `armeabi-v7a`, `x86_64` (фильтр в `android/app/build.gradle.kts`). Устройства x86 не поддерживаются.

## Известные ограничения

- На Android 8.0 (API 26) Oboe, вероятно, использует OpenSL ES, где временные метки вывода могут отсутствовать: тогда звука не будет, а в интерфейсе будет «НЕТ ЗВУКА». Нужна проверка на таком телефоне; запасной вариант: поднять `minSdk` до 27.
- Приёмка на железе (блокировка экрана 2 часа, Bluetooth и проводные наушники, смена маршрута, потеря Wi-Fi, перезапуск хоста, QR с реального хоста, фокус и входящий звонок, батарейные «убийцы» производителей) ещё не проведена. Эмулятор без звука проверяет только то, что ничего не падает.
- Пароль пары отображается и хранится открытым текстом (локальная сеть, см. ADR 0007).
