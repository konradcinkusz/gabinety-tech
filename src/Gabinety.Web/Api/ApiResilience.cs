using Microsoft.Extensions.DependencyInjection;
using Polly;
using Microsoft.Extensions.Http.Resilience;

namespace Gabinety.Web.Api;

/// <summary>
/// Registration of the typed clients (BLAZOR-WEB-APP section 5). <c>Gabinety.ServiceDefaults</c> ALREADY adds the standard resilience handler (and service discovery) to every
/// HttpClient through <c>ConfigureHttpClientDefaults</c>, so a client registered here must NOT call AddStandardResilienceHandler again: a second handler multiplies the attempts
/// (4 x 4 on a failing call). A test walks the built handler chain of every client and counts exactly one. What this class does is tune the ONE handler per client to SPEC 5.1.
/// </summary>
public static class ApiResilience
{
    /// <summary>SPEC 5.1: web to Directory, Booking, Reputation reads: 5 s, once, GET only. The wait before the retry follows <c>Retry-After</c> (the services answer 429 under their per-client limit), at most 2 s.</summary>
    static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5), MaxRetryWait = TimeSpan.FromSeconds(2);

    public static IServiceCollection AddBackendClients(this IServiceCollection services)
    {
        services.AddTransient<BackendHeadersHandler>();
        services.AddScoped<VisitorAddressAccessor>();
        Add<DirectoryClient>(services, Backend.Directory);
        Add<BookingClient>(services, Backend.Booking);
        Add<ReputationClient>(services, Backend.Reputation);
        Add<AssistantClient>(services, Backend.Assistant); // status read (5 s) and the brief suggestion, which asks for its 35 s per request (SPEC 5.1)
        Add<BriefAssistantClient>(services, Backend.Assistant);   // the brief suggestion (35 s)
        Add<ReviewsClient>(services, Backend.Reputation);         // write a review, reply
        Add<MeClient>(services, Backend.Directory);               // who am I
        Add<Auth.IdentityClient>(services, Backend.Auth);
        services.AddScoped<AdminApi>();      // the admin pages' calls with the administrator's own bearer (Api/Admin)
        Add<AccountClient>(services, Backend.Auth); // the signed-in user's own account calls (me, consents, export, delete)
        Add<PractitionerClient>(services, Backend.Directory);              // the practitioner's own profile and offers (port 4)
        Add<PractitionerSchedulingClient>(services, Backend.Booking);      // availability and the payments account (port 4)
        // The options of the ONE handler the kernel added to every client. All backends share the SPEC 5.1 profile of a read: 5 s per attempt, ONE retry, and only for a GET or HEAD, so a login,
        // a refresh and every later write are never retried automatically (SPEC 5 rule 8), whatever the client. The wait before the retry follows Retry-After (the services answer 429 under
        // their per-client limit), at most 2 s. (Per-client names such as "directory-standard" are not what the kernel's default registration reads: ConfigureAll is.)
        services.ConfigureAll<HttpStandardResilienceOptions>(o =>
        {
            o.AttemptTimeout.Timeout = ReadTimeout;
            o.AttemptTimeout.TimeoutGenerator = args => ValueTask.FromResult(Budget(args.Context, ReadTimeout));
            o.Retry.MaxRetryAttempts = 1;
            o.Retry.DisableForUnsafeHttpMethods();
            o.Retry.ShouldRetryAfterHeader = true;
            o.Retry.MaxDelay = MaxRetryWait;
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            o.TotalRequestTimeout.TimeoutGenerator = args => ValueTask.FromResult(Budget(args.Context, TimeSpan.FromSeconds(15)) is var b && b > ReadTimeout ? b + TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(15));
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        });
        return services;
    }

    /// <summary>The budget a request asked for through <see cref="RequestContext.Timeout"/> (SPEC 5.1: the booking write 10 s, the assistant 35 s), else <paramref name="fallback"/>. It can only be raised for a write: a retry stays off for unsafe methods.</summary>
    static TimeSpan Budget(ResilienceContext context, TimeSpan fallback) =>
        context.GetRequestMessage() is { } request && request.Options.TryGetValue(RequestContext.Timeout, out var budget) && budget > TimeSpan.Zero && budget <= MaxBudget ? budget : fallback;

    public static readonly TimeSpan MaxBudget = TimeSpan.FromSeconds(40);

    static void Add<TClient>(IServiceCollection services, string name) where TClient : class =>
        services.AddHttpClient<TClient>(name, c => c.BaseAddress = new Uri(Backend.BaseAddress(name))).AddHttpMessageHandler<BackendHeadersHandler>();
}
