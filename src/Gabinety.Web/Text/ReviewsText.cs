namespace Gabinety.Web.Text;

/// <summary>The Polish text of reviews: the form of a client and the list of a practitioner (verbatim from <c>web/src/components/reviews/strings.ts</c> where a journey reads it, J3).</summary>
public static class ReviewsText
{
    public const string FormTitle = "Wystaw opinię", FormMetaTitle = "Wystaw opinię | Gabinety Tech", ListTitle = "Opinie o mnie", ListMetaTitle = "Opinie o mnie | Gabinety Tech";

    public static class Form
    {
        public const string Intro = "Opinia dotyczy odbytej konsultacji. Można ją wystawić raz.", RatingLegend = "Ocena", RatingOption = "{0} z 5", TextLabel = "Treść opinii";
        public const string TextHint = "Od 20 do 2000 znaków. Opisz, z czym przyszedłeś i jak przebiegła konsultacja.", Submit = "Wyślij opinię", Back = "Wróć do rezerwacji";
        public const string RatingRequired = "Wybierz ocenę od 1 do 5.", TextTooShort = "Opinia musi mieć co najmniej 20 znaków.", TextTooLong = "Opinia może mieć najwyżej 2000 znaków.";
        public const string TextInvalid = "Treść opinii nie została przyjęta. Popraw ją i spróbuj ponownie.", Counter = "{0} / 2000", ScaleHint = "1 oznacza „słabo”, 5 oznacza „świetnie”.";
    }

    /// <summary>The words of the five-point scale: all five are rendered and CSS shows only the one of the checked rating.</summary>
    public static readonly string[] Scale = ["Słabo", "Przeciętnie", "Dobrze", "Bardzo dobrze", "Świetnie"];

    public static class Done
    {
        public const string Title = "Dziękujemy za opinię", Held = "Opinia czeka na sprawdzenie. Pojawi się na profilu programisty dopiero po zatwierdzeniu i może nie zostać opublikowana.";
        public const string Published = "Opinia została opublikowana na profilu programisty.";
    }

    public static class Already { public const string Title = "Ta konsultacja ma już opinię", Body = "Do jednej rezerwacji można wystawić tylko jedną opinię."; }

    public static class NotReviewable { public const string Title = "Tej konsultacji nie można ocenić", Body = "Opinię można wystawić po zakończonej konsultacji, w ciągu 14 dni od jej zakończenia."; }

    public static class List
    {
        public const string Loading = "Ładowanie opinii…", EmptyTitle = "Brak opinii", Empty = "Nie masz jeszcze opublikowanych opinii.", NotPractitionerTitle = "Opinie widzą tu programiści";
        public const string NotPractitioner = "Tę stronę mają programiści z profilem. Jeśli chcesz oferować konsultacje, utwórz profil w gabinecie programisty.", CreateProfile = "Utwórz profil programisty";
        public const string Reviewer = "Zweryfikowany klient", Rating = "Ocena: {0} z 5", More = "Pokaż kolejne opinie", ReplyHeading = "Twoja odpowiedź";
    }

    public static class Reply
    {
        public const string Label = "Odpowiedź na opinię", Hint = "Od 10 do 1000 znaków. Odpowiedzieć można raz, odpowiedzi nie da się później zmienić.", Submit = "Wyślij odpowiedź";
        public const string TooShort = "Odpowiedź musi mieć co najmniej 10 znaków.", TooLong = "Odpowiedź może mieć najwyżej 1000 znaków.", Already = "Na tę opinię już odpowiedziano.", Done = "Odpowiedź została dodana.";
    }

    public static class Errors
    {
        public const string Network = "Brak połączenia. Sprawdź internet i spróbuj ponownie.", Unavailable = "Usługa jest chwilowo niedostępna. Spróbuj za chwilę.", NotFound = "Nie znaleziono tej rezerwacji.";
        public const string Forbidden = "Nie masz do tego uprawnień.", RateLimited = "Zbyt wiele prób. Spróbuj za chwilę.", Validation = "Dane są nieprawidłowe. Sprawdź formularz.", Generic = "Coś poszło nie tak. Spróbuj ponownie.";
    }
}
