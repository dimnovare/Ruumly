namespace Ruumly.Backend.Helpers;

public static class EmailTranslations
{
    public record EmailStrings(
        string PasswordResetSubject,
        string PasswordResetGreeting,
        string PasswordResetBody1,
        string PasswordResetBody2,
        string PasswordResetExpiry,
        string PasswordResetButton,
        string PasswordResetCopyLabel,
        string PasswordResetSecurityTitle,
        string PasswordResetSecurityBody,
        string PasswordResetContactUs,
        string PasswordResetFooter,
        string BookingConfirmSubject,
        string BookingConfirmGreeting,
        string BookingConfirmBody,
        string BookingConfirmService,
        string BookingConfirmStartDate,
        string BookingConfirmPeriod,
        string BookingConfirmTotal,
        string BookingConfirmVat,
        string BookingConfirmNext,
        string BookingConfirmViewButton,
        string BookingConfirmFooter
    );

    private static readonly EmailStrings Et = new(
        PasswordResetSubject:       "Ruumly — parooli taastamine",
        PasswordResetGreeting:      "Tere,",
        PasswordResetBody1:         "Saime parooli taastamise taotluse teie Ruumly kontole",
        PasswordResetBody2:         "Klikkige alloleval nupul parooli vahetamiseks.",
        PasswordResetExpiry:        "Link kehtib <strong>2 tundi</strong>.",
        PasswordResetButton:        "Vaheta parool",
        PasswordResetCopyLabel:     "Või kopeerige see link oma brauserisse:",
        PasswordResetSecurityTitle: "⚠ Kui te seda taotlust ei teinud",
        PasswordResetSecurityBody:
            "Ignoreerige seda e-kirja — teie parool jääb muutmata ja keegi teine ei pääse " +
            "teie kontole ligi. Kui kahtlustate, et keegi üritab teie kontot kasutada, " +
            "võtke meiega ühendust:",
        PasswordResetContactUs:     "info@ruumly.eu",
        PasswordResetFooter:        "See on automaatne e-kiri. Palun ärge vastake sellele.",
        BookingConfirmSubject:      "Ruumly — broneeringu kinnitus",
        BookingConfirmGreeting:     "Tere",
        BookingConfirmBody:         "Teie broneeringu taotlus on vastu võetud.",
        BookingConfirmService:      "Teenus",
        BookingConfirmStartDate:    "Alguskuupäev",
        BookingConfirmPeriod:       "Periood",
        BookingConfirmTotal:        "Kokku",
        BookingConfirmVat:          "sisaldab KM",
        BookingConfirmNext:
            "Partner võtab teiega ühendust kinnitamisel. Broneeringu staatust " +
            "saate jälgida oma kontol.",
        BookingConfirmViewButton:   "Vaata broneeringut",
        BookingConfirmFooter:       "See on automaatne e-kiri. Palun ärge vastake sellele."
    );

    private static readonly EmailStrings En = new(
        PasswordResetSubject:       "Ruumly — password reset",
        PasswordResetGreeting:      "Hello,",
        PasswordResetBody1:         "We received a password reset request for your Ruumly account",
        PasswordResetBody2:         "Click the button below to reset your password.",
        PasswordResetExpiry:        "The link is valid for <strong>2 hours</strong>.",
        PasswordResetButton:        "Reset password",
        PasswordResetCopyLabel:     "Or copy this link into your browser:",
        PasswordResetSecurityTitle: "⚠ If you didn't request this",
        PasswordResetSecurityBody:
            "Ignore this email — your password will remain unchanged and no one " +
            "can access your account without it. If you suspect someone is trying " +
            "to access your account, contact us:",
        PasswordResetContactUs:     "info@ruumly.eu",
        PasswordResetFooter:        "This is an automated email. Please do not reply.",
        BookingConfirmSubject:      "Ruumly — booking confirmation",
        BookingConfirmGreeting:     "Hello",
        BookingConfirmBody:         "Your booking request has been received.",
        BookingConfirmService:      "Service",
        BookingConfirmStartDate:    "Start date",
        BookingConfirmPeriod:       "Period",
        BookingConfirmTotal:        "Total",
        BookingConfirmVat:          "incl. VAT",
        BookingConfirmNext:
            "The partner will contact you upon confirmation. You can track your " +
            "booking status in your account.",
        BookingConfirmViewButton:   "View booking",
        BookingConfirmFooter:       "This is an automated email. Please do not reply."
    );

    private static readonly EmailStrings Ru = new(
        PasswordResetSubject:       "Ruumly — восстановление пароля",
        PasswordResetGreeting:      "Здравствуйте,",
        PasswordResetBody1:         "Мы получили запрос на восстановление пароля для вашего аккаунта Ruumly",
        PasswordResetBody2:         "Нажмите кнопку ниже, чтобы сменить пароль.",
        PasswordResetExpiry:        "Ссылка действительна <strong>2 часа</strong>.",
        PasswordResetButton:        "Сменить пароль",
        PasswordResetCopyLabel:     "Или скопируйте эту ссылку в браузер:",
        PasswordResetSecurityTitle: "⚠ Если вы не делали этот запрос",
        PasswordResetSecurityBody:
            "Проигнорируйте это письмо — ваш пароль останется прежним. Если вы подозреваете, " +
            "что кто-то пытается получить доступ к вашему аккаунту, свяжитесь с нами:",
        PasswordResetContactUs:     "info@ruumly.eu",
        PasswordResetFooter:        "Это автоматическое письмо. Пожалуйста, не отвечайте на него.",
        BookingConfirmSubject:      "Ruumly — подтверждение бронирования",
        BookingConfirmGreeting:     "Здравствуйте",
        BookingConfirmBody:         "Ваш запрос на бронирование получен.",
        BookingConfirmService:      "Услуга",
        BookingConfirmStartDate:    "Дата начала",
        BookingConfirmPeriod:       "Период",
        BookingConfirmTotal:        "Итого",
        BookingConfirmVat:          "включая НДС",
        BookingConfirmNext:
            "Партнёр свяжется с вами при подтверждении. Статус бронирования " +
            "можно отслеживать в личном кабинете.",
        BookingConfirmViewButton:   "Посмотреть бронирование",
        BookingConfirmFooter:       "Это автоматическое письмо. Пожалуйста, не отвечайте на него."
    );

    public static EmailStrings For(string? lang) =>
        lang switch
        {
            "en" => En,
            "ru" => Ru,
            _    => Et,   // default Estonian
        };
}
