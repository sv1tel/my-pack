using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace PackLauncher.Services;

public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    public static readonly (string Code, string Name)[] Languages =
    [
        ("ru", "Русский"),
        ("uk", "Українська"),
        ("en", "English")
    ];

    public string Language { get; private set; } = "ru";

    public string this[string key] =>
        Strings[Language].TryGetValue(key, out var value) ? value
        : Strings["en"].TryGetValue(key, out value) ? value
        : key;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action? LanguageChanged;

    public void SetLanguage(string? code)
    {
        Language = Strings.ContainsKey(code ?? "") ? code! : DetectSystemLanguage();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    public static string DetectSystemLanguage() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "ru" or "be" or "kk" => "ru",
        "uk" => "uk",
        _ => "en"
    };

    public static string T(string key) => I[key];

    public static string F(string key, params object[] args) => string.Format(I[key], args);

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["ru"] = new()
        {
            ["err_rate_limit"] = "GitHub временно ограничил запросы, попробуйте через час",
            ["err_rate_limit_until"] = "GitHub временно ограничил запросы, попробуйте после {0}",
            ["update"] = "Обновить",
            ["status_checking"] = "Проверка обновлений…",
            ["status_pack_update"] = "Доступно обновление сборки {0}",
            ["status_launcher_update"] = "Доступен лаунчер {0}",
            ["status_pack_updated"] = "Сборка обновлена до {0}",
            ["err_tree_truncated"] = "В сборке слишком много файлов для GitHub API",
            ["err_bad_path"] = "Недопустимый путь в сборке: {0}",
            ["s_update_launcher"] = "Обновить лаунчер",
            ["s_up_to_date_version"] = "У вас последняя версия ({0})",
            ["s_close_game_first"] = "Сначала закройте игру",
            ["s_installing_version"] = "Устанавливается версия {0}, лаунчер перезапустится…",
            ["nick_placeholder"] = "Никнейм",
            ["nick_invalid"] = "Никнейм: от 3 до 16 символов, только латиница, цифры и _",
            ["play"] = "Играть",
            ["close"] = "Закрыть",
            ["loading"] = "Загрузка…",
            ["settings"] = "Настройки",
            ["version"] = "Версия {0}",
            ["new_version"] = "Новая версия {0}",
            ["gb"] = "ГБ",
            ["status_starting"] = "Запуск игры…",
            ["status_running"] = "Игра запущена",
            ["status_downloading"] = "Загрузка файлов ({0}/{1}): {2}",
            ["status_done"] = "Готово",
            ["status_neoforge"] = "Установка NeoForge",
            ["status_game_files"] = "Проверка файлов игры",
            ["status_launcher_download"] = "Загрузка обновления лаунчера… {0:P0}",
            ["status_offline"] = "Нет связи с сервером обновлений",
            ["repo_not_configured"] = "Укажите свой GitHub в AppConfig.cs",
            ["err_launch"] = "Не удалось запустить игру",
            ["err_update"] = "Не удалось обновить",
            ["err_hash"] = "Файл {0} скачался с ошибкой. Попробуйте ещё раз через пару минут.",
            ["no_manifest"] = "Нет информации о сборке. Проверьте подключение к интернету.",
            ["game_crashed"] = "Игра завершилась с ошибкой (код {0}).\nОткрыть папку с логами?",
            ["s_language"] = "Язык",
            ["s_memory"] = "Память",
            ["s_pc_ram"] = "Сколько оперативной памяти у вашего компьютера",
            ["s_ram_summary"] = "Обнаружено {0} ГБ · игре будет выделено {1}",
            ["s_ram_32"] = "32 ГБ и больше",
            ["s_yours"] = "у вас",
            ["s_commands"] = "Команды запуска",
            ["s_commands_desc"] = "Флаги Java. Память (-Xmx, -Xms) берётся из настройки выше.",
            ["s_reset"] = "Сбросить",
            ["s_game"] = "Игра",
            ["s_window_size"] = "Размер окна игры",
            ["s_fullscreen"] = "Полноэкранный режим",
            ["s_after_launch"] = "После запуска игры",
            ["s_after_minimize"] = "Свернуть лаунчер",
            ["s_after_close"] = "Закрыть лаунчер",
            ["s_after_nothing"] = "Ничего не делать",
            ["s_game_dir"] = "Папка игры",
            ["s_open"] = "Открыть",
            ["s_change"] = "Изменить",
            ["s_folder_dialog"] = "Где хранить игру",
            ["s_maintenance"] = "Обслуживание",
            ["s_verify"] = "Проверить файлы игры",
            ["s_verify_desc"] = "Заново скачает повреждённые или изменённые файлы",
            ["s_verify_btn"] = "Проверить",
            ["s_logs"] = "Логи игры",
            ["s_appearance"] = "Оформление",
            ["s_theme"] = "Тема",
            ["s_theme_system"] = "Как в системе",
            ["s_theme_light"] = "Светлая",
            ["s_theme_dark"] = "Тёмная",
            ["s_about"] = "О лаунчере",
            ["s_checking"] = "Проверка…"
        },

        ["uk"] = new()
        {
            ["err_rate_limit"] = "GitHub тимчасово обмежив запити, спробуйте за годину",
            ["err_rate_limit_until"] = "GitHub тимчасово обмежив запити, спробуйте після {0}",
            ["update"] = "Оновити",
            ["status_checking"] = "Перевірка оновлень…",
            ["status_pack_update"] = "Доступне оновлення збірки {0}",
            ["status_launcher_update"] = "Доступний лаунчер {0}",
            ["status_pack_updated"] = "Збірку оновлено до {0}",
            ["err_tree_truncated"] = "У збірці забагато файлів для GitHub API",
            ["err_bad_path"] = "Неприпустимий шлях у збірці: {0}",
            ["s_update_launcher"] = "Оновити лаунчер",
            ["s_up_to_date_version"] = "У вас остання версія ({0})",
            ["s_close_game_first"] = "Спочатку закрийте гру",
            ["s_installing_version"] = "Встановлюється версія {0}, лаунчер перезапуститься…",
            ["nick_placeholder"] = "Нікнейм",
            ["nick_invalid"] = "Нікнейм: від 3 до 16 символів, лише латиниця, цифри та _",
            ["play"] = "Грати",
            ["close"] = "Закрити",
            ["loading"] = "Завантаження…",
            ["settings"] = "Налаштування",
            ["version"] = "Версія {0}",
            ["new_version"] = "Нова версія {0}",
            ["gb"] = "ГБ",
            ["status_starting"] = "Запуск гри…",
            ["status_running"] = "Гру запущено",
            ["status_downloading"] = "Завантаження файлів ({0}/{1}): {2}",
            ["status_done"] = "Готово",
            ["status_neoforge"] = "Встановлення NeoForge",
            ["status_game_files"] = "Перевірка файлів гри",
            ["status_launcher_download"] = "Завантаження оновлення лаунчера… {0:P0}",
            ["status_offline"] = "Немає зв'язку із сервером оновлень",
            ["repo_not_configured"] = "Вкажіть GitHub в AppConfig.cs",
            ["err_launch"] = "Не вдалося запустити гру",
            ["err_update"] = "Не вдалося оновити",
            ["err_hash"] = "Файл {0} завантажився з помилкою. Спробуйте ще раз за кілька хвилин.",
            ["no_manifest"] = "Немає інформації про збірку. Перевірте підключення до інтернету.",
            ["game_crashed"] = "Гра завершилася з помилкою (код {0}).\nВідкрити теку з логами?",
            ["s_language"] = "Мова",
            ["s_memory"] = "Пам'ять",
            ["s_pc_ram"] = "Скільки оперативної пам'яті у вашого комп'ютера",
            ["s_ram_summary"] = "Виявлено {0} ГБ · грі буде виділено {1}",
            ["s_ram_32"] = "32 ГБ і більше",
            ["s_yours"] = "у вас",
            ["s_commands"] = "Команди запуску",
            ["s_commands_desc"] = "Прапорці Java. Пам'ять (-Xmx, -Xms) береться з налаштування вище.",
            ["s_reset"] = "Скинути",
            ["s_game"] = "Гра",
            ["s_window_size"] = "Розмір вікна гри",
            ["s_fullscreen"] = "Повноекранний режим",
            ["s_after_launch"] = "Після запуску гри",
            ["s_after_minimize"] = "Згорнути лаунчер",
            ["s_after_close"] = "Закрити лаунчер",
            ["s_after_nothing"] = "Нічого не робити",
            ["s_game_dir"] = "Тека гри",
            ["s_open"] = "Відкрити",
            ["s_change"] = "Змінити",
            ["s_folder_dialog"] = "Де зберігати гру",
            ["s_maintenance"] = "Обслуговування",
            ["s_verify"] = "Перевірити файли гри",
            ["s_verify_desc"] = "Знову завантажить пошкоджені або змінені файли",
            ["s_verify_btn"] = "Перевірити",
            ["s_logs"] = "Логи гри",
            ["s_appearance"] = "Оформлення",
            ["s_theme"] = "Тема",
            ["s_theme_system"] = "Як у системі",
            ["s_theme_light"] = "Світла",
            ["s_theme_dark"] = "Темна",
            ["s_about"] = "Про лаунчер",
            ["s_checking"] = "Перевірка…"
        },

        ["en"] = new()
        {
            ["err_rate_limit"] = "GitHub is limiting requests, try again in an hour",
            ["err_rate_limit_until"] = "GitHub is limiting requests, try again after {0}",
            ["update"] = "Update",
            ["status_checking"] = "Checking for updates…",
            ["status_pack_update"] = "Pack update {0} is available",
            ["status_launcher_update"] = "Launcher {0} is available",
            ["status_pack_updated"] = "Pack updated to {0}",
            ["err_tree_truncated"] = "The pack has too many files for the GitHub API",
            ["err_bad_path"] = "Invalid path in the pack: {0}",
            ["s_update_launcher"] = "Update launcher",
            ["s_up_to_date_version"] = "You're on the latest version ({0})",
            ["s_close_game_first"] = "Close the game first",
            ["s_installing_version"] = "Installing version {0}, the launcher will restart…",
            ["nick_placeholder"] = "Nickname",
            ["nick_invalid"] = "Nickname: 3 to 16 characters, only Latin letters, digits and _",
            ["play"] = "Play",
            ["close"] = "Close",
            ["loading"] = "Loading…",
            ["settings"] = "Settings",
            ["version"] = "Version {0}",
            ["new_version"] = "New version {0}",
            ["gb"] = "GB",
            ["status_starting"] = "Starting the game…",
            ["status_running"] = "The game is running",
            ["status_downloading"] = "Downloading files ({0}/{1}): {2}",
            ["status_done"] = "Done",
            ["status_neoforge"] = "Installing NeoForge",
            ["status_game_files"] = "Checking game files",
            ["status_launcher_download"] = "Downloading launcher update… {0:P0}",
            ["status_offline"] = "Can't reach the update server",
            ["repo_not_configured"] = "Set your GitHub in AppConfig.cs",
            ["err_launch"] = "Failed to start the game",
            ["err_update"] = "Update failed",
            ["err_hash"] = "File {0} was damaged while downloading. Try again in a couple of minutes.",
            ["no_manifest"] = "No modpack info. Check your internet connection.",
            ["game_crashed"] = "The game exited with an error (code {0}).\nOpen the logs folder?",
            ["s_language"] = "Language",
            ["s_memory"] = "Memory",
            ["s_pc_ram"] = "How much RAM does your computer have",
            ["s_ram_summary"] = "Detected {0} GB · the game gets {1}",
            ["s_ram_32"] = "32 GB or more",
            ["s_yours"] = "yours",
            ["s_commands"] = "Launch commands",
            ["s_commands_desc"] = "Java flags. Memory (-Xmx, -Xms) comes from the setting above.",
            ["s_reset"] = "Reset",
            ["s_game"] = "Game",
            ["s_window_size"] = "Game window size",
            ["s_fullscreen"] = "Fullscreen",
            ["s_after_launch"] = "After the game starts",
            ["s_after_minimize"] = "Minimize the launcher",
            ["s_after_close"] = "Close the launcher",
            ["s_after_nothing"] = "Do nothing",
            ["s_game_dir"] = "Game folder",
            ["s_open"] = "Open",
            ["s_change"] = "Change",
            ["s_folder_dialog"] = "Where to keep the game",
            ["s_maintenance"] = "Maintenance",
            ["s_verify"] = "Verify game files",
            ["s_verify_desc"] = "Re-downloads damaged or changed files",
            ["s_verify_btn"] = "Verify",
            ["s_logs"] = "Game logs",
            ["s_appearance"] = "Appearance",
            ["s_theme"] = "Theme",
            ["s_theme_system"] = "Use system setting",
            ["s_theme_light"] = "Light",
            ["s_theme_dark"] = "Dark",
            ["s_about"] = "About",
            ["s_checking"] = "Checking…"
        }
    };
}

public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
