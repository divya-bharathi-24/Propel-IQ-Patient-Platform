using Propel.Modules.Calendar.Interfaces;
using StackExchange.Redis;

namespace Propel.Modules.Calendar.Services;

/// <summary>
/// Redis-backed OAuth state service for production.
/// Uses SETEX (atomic set-with-TTL) on write and a Lua script for atomic GET+DEL on read
/// to guarantee one-time-use semantics (OWASP A07).
/// </summary>
public sealed class RedisOAuthStateService : IOAuthStateService
{
    private readonly IConnectionMultiplexer _redis;
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    // Lua script: atomically GET and DEL in one round-trip (one-time-use guarantee, OWASP A07)
    private const string GetAndDeleteScript = @"
local v = redis.call('GET', KEYS[1])
if v then
  redis.call('DEL', KEYS[1])
end
return v";

    public RedisOAuthStateService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task SetAsync(string stateKey, string payload, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync($"oauth:state:{stateKey}", payload, StateTtl);
    }

    public async Task<string?> GetAndDeleteAsync(string stateKey, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var result = await db.ScriptEvaluateAsync(
            GetAndDeleteScript,
            keys: [$"oauth:state:{stateKey}"]);
        return result.IsNull ? null : (string?)result;
    }
}
