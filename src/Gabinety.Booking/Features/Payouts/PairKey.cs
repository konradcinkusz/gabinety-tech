using System.Security.Cryptography;
using System.Text;

namespace Gabinety.Booking.Features.Payouts;

/// <summary>
/// The <c>pair_key</c> of a <c>PlatformFee</c> ledger row (CONTRACTS 4.1). The spec format is <c>&lt;client_user_id&gt;:&lt;practitioner_id&gt;</c> in a
/// column of 80 characters. When that string fits it is used as is; when it does not (a long client user id) the key is
/// <c>h:</c> + the lowercase hex SHA-256 of the UTF-8 bytes of that string (66 characters). The mapping is deterministic and collision-resistant,
/// so the unique partial index still decides "one fee per pair" and a long id can never make a transition fail.
/// </summary>
public static class PairKey
{
    public const int MaxLength = 80;

    public static string For(string clientUserId, Guid practitionerId)
    {
        var spec = $"{clientUserId}:{practitionerId}";
        return spec.Length <= MaxLength ? spec : "h:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(spec)));
    }
}
