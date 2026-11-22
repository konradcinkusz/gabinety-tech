namespace Gabinety.DemoSeeder;

public enum ProposalOutcome { Open, Countered, Accepted }

/// <summary>
/// One negotiation the seeder creates (ADR proposal 0013): who proposes to whom, the terms, and how far it goes. Everything is a demo- name and synthetic text with no e-mail, phone number or link
/// (the service refuses those). The practitioners must advertise contract availability in <see cref="DemoCatalog"/>, or the service answers 422.
/// </summary>
public sealed record ProposalThread(string Client, string Practitioner, string Kind, long RateMinor, int? HoursPerWeek, int? TotalHours, int? DurationMonths, int StartInDays,
    string Text, string? Company, ProposalOutcome Outcome, long CounterRateMinor = 0, string? CounterText = null);

public static class ProposalPlan
{
    public static readonly ProposalThread[] All =
    [
        // waiting for the programmer: the programmer's inbox shows "Twoja kolej", the client's list "Czekasz na odpowiedź"
        new("demo-client", "demo-practitioner-1", "LongTerm", 15000, 20, null, 6, 30,
            "Szukamy programisty .NET do dwudziestu godzin tygodniowo przy modernizacji systemu rezerwacji. Zespół jest mały, a kod wymaga uporządkowania.", "Demo Sp. z o.o.", ProposalOutcome.Open),
        // the programmer answered with other terms: now it is the client's turn
        new("demo-client-2", "demo-practitioner-2", "LongTerm", 13000, 12, null, 4, 45,
            "Potrzebujemy pomocy przy migracji starej aplikacji do nowoczesnego .NET. Szukamy kogoś na kilka miesięcy, kilkanaście godzin tygodniowo.", null, ProposalOutcome.Countered,
            14000, "Proponuję nieco wyższą stawkę i dziesięć godzin tygodniowo, bo tyle realnie mogę poświęcić."),
        // both sides agreed: the contact section appears and the thread is closed
        new("demo-client-3", "demo-practitioner-5", "ShortTerm", 12000, null, 30, null, 14,
            "Chcemy przejrzeć architekturę nowego modułu przed jego wdrożeniem. Trzydzieści godzin w ciągu miesiąca powinno wystarczyć.", "Demo Studio", ProposalOutcome.Accepted),
    ];
}
