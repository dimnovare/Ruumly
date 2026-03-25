namespace Ruumly.Backend.Helpers;

public static class ErrorMessages
{
    public static string Get(string key, string? lang)
    {
        var l = Normalise(lang);
        return l switch
        {
            "en" => En.GetValueOrDefault(key, key),
            "ru" => Ru.GetValueOrDefault(key, key),
            _    => Et.GetValueOrDefault(key, key),
        };
    }

    private static string Normalise(string? lang)
    {
        if (lang is null) return "et";
        var s = lang.ToLowerInvariant().Split('-')[0]
                                      .Split(',')[0]
                                      .Trim();
        return s is "en" or "ru" ? s : "et";
    }

    private static readonly Dictionary<string, string> Et = new()
    {
        ["EMAIL_ALREADY_REGISTERED"]    = "See e-posti aadress on juba registreeritud.",
        ["INVALID_CREDENTIALS"]         = "Vale e-post või parool.",
        ["ACCOUNT_BLOCKED"]             = "Teie konto on blokeeritud. Võtke meiega ühendust.",
        ["INVALID_REFRESH_TOKEN"]       = "Sessioon on aegunud. Palun logi uuesti sisse.",
        ["INVALID_GOOGLE_TOKEN"]        = "Google'i sisselogimine ebaõnnestus.",
        ["INVITE_CODE_REQUIRED"]        = "Registreerimine on hetkel ainult kutsega.",
        ["INVALID_INVITE_CODE"]         = "Vale kutse kood. Kontrollige koodi õigsust.",
        ["LISTING_NOT_FOUND"]           = "Kuulutust ei leitud.",
        ["LOCATION_NOT_FOUND"]          = "Asukohta ei leitud.",
        ["NO_UNITS_AVAILABLE"]          = "Valitud perioodil pole seda tüüpi vabu ühikuid. Valige teine kuupäev.",
        ["BOOKING_NOT_FOUND"]           = "Broneeringut ei leitud.",
        ["ORDER_NOT_FOUND"]             = "Tellimust ei leitud.",
        ["ORDER_WRONG_STATUS"]          = "Tellimust ei saa selles staatuses muuta.",
        ["PASSWORD_TOO_SHORT"]          = "Parool peab olema vähemalt 8 tähemärki.",
        ["CURRENT_PASSWORD_REQUIRED"]   = "Praeguse parooli sisestamine on kohustuslik.",
        ["CURRENT_PASSWORD_WRONG"]      = "Praegune parool on vale.",
        ["PASSWORD_SAME_AS_OLD"]        = "Uus parool peab erinema praegusest.",
        ["USER_NOT_FOUND"]              = "Kasutajat ei leitud.",
        ["TIER_LOCATION_LIMIT"]         = "Teie plaan lubab kuni {0} aktiivset asukohta. Uuendage plaani.",
        ["PAYMENT_PROVIDER_UNAVAILABLE"]= "Makseteenus on hetkel kättesaamatu. Proovige hiljem uuesti.",
        ["INVOICE_NOT_FOUND"]           = "Arvet ei leitud.",
        ["INVALID_DATE_FORMAT"]         = "Vale kuupäeva formaat. Kasutage yyyy-MM-dd.",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["EMAIL_ALREADY_REGISTERED"]    = "This email address is already registered.",
        ["INVALID_CREDENTIALS"]         = "Invalid email or password.",
        ["ACCOUNT_BLOCKED"]             = "Your account has been blocked. Please contact us.",
        ["INVALID_REFRESH_TOKEN"]       = "Session expired. Please log in again.",
        ["INVALID_GOOGLE_TOKEN"]        = "Google login failed.",
        ["INVITE_CODE_REQUIRED"]        = "Registration is currently by invitation only.",
        ["INVALID_INVITE_CODE"]         = "Invalid invite code. Please check the code.",
        ["LISTING_NOT_FOUND"]           = "Listing not found.",
        ["LOCATION_NOT_FOUND"]          = "Location not found.",
        ["NO_UNITS_AVAILABLE"]          = "No units of this type are available for the selected period. Please choose a different date.",
        ["BOOKING_NOT_FOUND"]           = "Booking not found.",
        ["ORDER_NOT_FOUND"]             = "Order not found.",
        ["ORDER_WRONG_STATUS"]          = "Order cannot be changed in its current status.",
        ["PASSWORD_TOO_SHORT"]          = "Password must be at least 8 characters.",
        ["CURRENT_PASSWORD_REQUIRED"]   = "Current password is required.",
        ["CURRENT_PASSWORD_WRONG"]      = "Current password is incorrect.",
        ["PASSWORD_SAME_AS_OLD"]        = "New password must differ from the current one.",
        ["USER_NOT_FOUND"]              = "User not found.",
        ["TIER_LOCATION_LIMIT"]         = "Your plan allows up to {0} active locations. Please upgrade your plan.",
        ["PAYMENT_PROVIDER_UNAVAILABLE"]= "Payment service is currently unavailable. Please try again later.",
        ["INVOICE_NOT_FOUND"]           = "Invoice not found.",
        ["INVALID_DATE_FORMAT"]         = "Invalid date format. Use yyyy-MM-dd.",
    };

    private static readonly Dictionary<string, string> Ru = new()
    {
        ["EMAIL_ALREADY_REGISTERED"]    = "Этот адрес электронной почты уже зарегистрирован.",
        ["INVALID_CREDENTIALS"]         = "Неверный email или пароль.",
        ["ACCOUNT_BLOCKED"]             = "Ваш аккаунт заблокирован. Свяжитесь с нами.",
        ["INVALID_REFRESH_TOKEN"]       = "Сессия истекла. Пожалуйста, войдите снова.",
        ["INVALID_GOOGLE_TOKEN"]        = "Ошибка входа через Google.",
        ["INVITE_CODE_REQUIRED"]        = "Регистрация только по приглашению.",
        ["INVALID_INVITE_CODE"]         = "Неверный код приглашения. Проверьте код.",
        ["LISTING_NOT_FOUND"]           = "Объявление не найдено.",
        ["LOCATION_NOT_FOUND"]          = "Локация не найдена.",
        ["NO_UNITS_AVAILABLE"]          = "На выбранный период нет свободных единиц данного типа. Выберите другую дату.",
        ["BOOKING_NOT_FOUND"]           = "Бронирование не найдено.",
        ["ORDER_NOT_FOUND"]             = "Заказ не найден.",
        ["ORDER_WRONG_STATUS"]          = "Заказ нельзя изменить в текущем статусе.",
        ["PASSWORD_TOO_SHORT"]          = "Пароль должен содержать не менее 8 символов.",
        ["CURRENT_PASSWORD_REQUIRED"]   = "Требуется текущий пароль.",
        ["CURRENT_PASSWORD_WRONG"]      = "Текущий пароль неверен.",
        ["PASSWORD_SAME_AS_OLD"]        = "Новый пароль должен отличаться от текущего.",
        ["USER_NOT_FOUND"]              = "Пользователь не найден.",
        ["TIER_LOCATION_LIMIT"]         = "Ваш план позволяет до {0} активных локаций. Обновите план.",
        ["PAYMENT_PROVIDER_UNAVAILABLE"]= "Сервис оплаты недоступен. Попробуйте позже.",
        ["INVOICE_NOT_FOUND"]           = "Счёт не найден.",
        ["INVALID_DATE_FORMAT"]         = "Неверный формат даты. Используйте yyyy-MM-dd.",
    };
}
