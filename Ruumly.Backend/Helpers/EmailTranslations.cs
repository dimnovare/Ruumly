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
        string BookingConfirmFooter,
        // Email verification
        string EmailVerifySubject,
        string EmailVerifyGreeting,
        string EmailVerifyBody,
        string EmailVerifyButton,
        string EmailVerifyExpiry,
        string EmailVerifyFooter,
        // Booking status update emails
        string BookingStatusConfirmedSubject,
        string BookingStatusConfirmedBody,
        string BookingStatusRejectedSubject,
        string BookingStatusRejectedBody,
        string BookingStatusCompletedSubject,
        string BookingStatusCompletedBody,
        string BookingStatusCancelledSubject,
        string BookingStatusCancelledBody,
        string BookingStatusViewLink
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
        EmailVerifySubject:         "Ruumly — kinnitage oma e-posti aadress",
        EmailVerifyGreeting:        "Tere,",
        EmailVerifyBody:            "Täname registreerumise eest! Konto aktiveerimiseks kinnitage oma e-posti aadress.",
        EmailVerifyButton:          "Kinnita e-post",
        EmailVerifyExpiry:          "Link kehtib 24 tundi.",
        EmailVerifyFooter:          "See on automaatne e-kiri. Palun ärge vastake sellele.",
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
        BookingConfirmFooter:       "See on automaatne e-kiri. Palun ärge vastake sellele.",
        BookingStatusConfirmedSubject: "Ruumly — broneering kinnitatud",
        BookingStatusConfirmedBody:    "Teie broneering #{id} on kinnitatud!",
        BookingStatusRejectedSubject:  "Ruumly — broneering tagasi lükatud",
        BookingStatusRejectedBody:     "Teie broneering #{id} on kahjuks tagasi lükatud",
        BookingStatusCompletedSubject: "Ruumly — broneering lõpetatud",
        BookingStatusCompletedBody:    "Teie broneering #{id} on lõpetatud",
        BookingStatusCancelledSubject: "Ruumly — broneering tühistatud",
        BookingStatusCancelledBody:    "Teie broneering #{id} on tühistatud",
        BookingStatusViewLink:         "Vaata broneeringut"
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
        EmailVerifySubject:         "Ruumly — verify your email address",
        EmailVerifyGreeting:        "Hello,",
        EmailVerifyBody:            "Thanks for signing up! Please verify your email address to activate your account.",
        EmailVerifyButton:          "Verify email",
        EmailVerifyExpiry:          "This link is valid for 24 hours.",
        EmailVerifyFooter:          "This is an automated email. Please do not reply.",
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
        BookingConfirmFooter:       "This is an automated email. Please do not reply.",
        BookingStatusConfirmedSubject: "Ruumly — booking confirmed",
        BookingStatusConfirmedBody:    "Your booking #{id} has been confirmed!",
        BookingStatusRejectedSubject:  "Ruumly — booking rejected",
        BookingStatusRejectedBody:     "Unfortunately, your booking #{id} has been rejected",
        BookingStatusCompletedSubject: "Ruumly — booking completed",
        BookingStatusCompletedBody:    "Your booking #{id} has been completed",
        BookingStatusCancelledSubject: "Ruumly — booking cancelled",
        BookingStatusCancelledBody:    "Your booking #{id} has been cancelled",
        BookingStatusViewLink:         "View booking"
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
        EmailVerifySubject:         "Ruumly — подтвердите адрес электронной почты",
        EmailVerifyGreeting:        "Здравствуйте,",
        EmailVerifyBody:            "Спасибо за регистрацию! Пожалуйста, подтвердите адрес электронной почты для активации аккаунта.",
        EmailVerifyButton:          "Подтвердить email",
        EmailVerifyExpiry:          "Ссылка действительна 24 часа.",
        EmailVerifyFooter:          "Это автоматическое письмо. Пожалуйста, не отвечайте на него.",
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
        BookingConfirmFooter:       "Это автоматическое письмо. Пожалуйста, не отвечайте на него.",
        BookingStatusConfirmedSubject: "Ruumly — бронирование подтверждено",
        BookingStatusConfirmedBody:    "Ваше бронирование #{id} подтверждено!",
        BookingStatusRejectedSubject:  "Ruumly — бронирование отклонено",
        BookingStatusRejectedBody:     "К сожалению, ваше бронирование #{id} было отклонено",
        BookingStatusCompletedSubject: "Ruumly — бронирование завершено",
        BookingStatusCompletedBody:    "Ваше бронирование #{id} завершено",
        BookingStatusCancelledSubject: "Ruumly — бронирование отменено",
        BookingStatusCancelledBody:    "Ваше бронирование #{id} отменено",
        BookingStatusViewLink:         "Посмотреть бронирование"
    );

    public static EmailStrings For(string? lang) =>
        lang switch
        {
            "en" => En,
            "ru" => Ru,
            _    => Et,   // default Estonian
        };
}
