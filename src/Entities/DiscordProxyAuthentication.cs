// Copyright (C) 2025 Lala Sabathil <aiko@aitsys.dev>
// Licensed under the AGPL-3.0-or-later
// See <https://www.gnu.org/licenses/> for details.

using System.Text.Json;

using Microsoft.AspNetCore.Http;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace AITSYS.Discord.LibraryDevelopmentTracking.Entities;

internal static class DiscordProxyAuthentication
{
	internal static bool ShouldValidate(HttpRequest request, Config config)
		=> !HostAllowlist.IsLocalHost(request.Host.Host)
			&& HostAllowlist.IsDiscordProxyHost(request.Host.Host, config);

	internal static bool ValidateProxyRequest(HttpRequest request, Config config, out string failureReason, out bool success)
	{
		failureReason = string.Empty;

		var signatureHeader = request.Headers["X-Signature-Ed25519"].ToString();
		var timestampHeader = request.Headers["X-Signature-Timestamp"].ToString();
		var payloadHeader = request.Headers["X-Discord-Proxy-Payload"].ToString();

		if (string.IsNullOrWhiteSpace(config.DiscordConfig.DiscordPublicKey))
		{
			failureReason = "activity.discord_public_key is not configured.";
			success = false;
			return false;
		}

		if (string.IsNullOrWhiteSpace(signatureHeader)
			|| string.IsNullOrWhiteSpace(timestampHeader)
			|| string.IsNullOrWhiteSpace(payloadHeader))
		{
			failureReason = "Missing Discord proxy authentication headers.";
			success = false;
			return false;
		}

		byte[] payloadBytes;
		try
		{
			payloadBytes = Convert.FromBase64String(payloadHeader);
		}
		catch (FormatException)
		{
			failureReason = "Discord proxy payload was not valid base64.";
			success = false;
			return false;
		}

		JsonElement payloadData;
		try
		{
			using var document = JsonDocument.Parse(payloadBytes);
			payloadData = document.RootElement.Clone();
		}
		catch (JsonException)
		{
			failureReason = "Discord proxy payload was not valid JSON.";
			success = false;
			return false;
		}

		if (!TryReadUnixTimestamp(payloadData, "created_at", out var createdAt))
		{
			failureReason = "Discord proxy payload is missing created_at.";
			success = false;
			return false;
		}

		if (!string.Equals(createdAt.ToString(), timestampHeader, StringComparison.Ordinal))
		{
			failureReason = "Discord proxy timestamp did not match the signed payload.";
			success = false;
			return false;
		}

		if (!TryReadUnixTimestamp(payloadData, "expires_at", out var expiresAt))
		{
			failureReason = "Discord proxy payload is missing expires_at.";
			success = false;
			return false;
		}

		if (expiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
		{
			failureReason = "Discord proxy authentication token has expired.";
			success = false;
			return false;
		}

		byte[] signatureBytes;
		try
		{
			signatureBytes = TryDecodeSignature(signatureHeader);
		}
		catch (FormatException)
		{
			failureReason = "Discord proxy signature was neither valid base64 nor hex.";
			success = false;
			return false;
		}

		if (!VerifyEd25519Signature(signatureBytes, payloadBytes, config.DiscordConfig.DiscordPublicKey))
		{
			failureReason = "Discord proxy signature verification failed.";
			success = false;
			return false;
		}
		
		success = true;
		return true;
	}

	private static bool TryReadUnixTimestamp(JsonElement payloadData, string propertyName, out long value)
	{
		value = 0;
		return payloadData.TryGetProperty(propertyName, out var property) && (property.ValueKind is JsonValueKind.Number
			? property.TryGetInt64(out value)
			: property.ValueKind is JsonValueKind.String && long.TryParse(property.GetString(), out value));
	}

	private static byte[] TryDecodeSignature(string signatureHeader)
	{
		try
		{
			return Convert.FromBase64String(signatureHeader);
		}
		catch (FormatException)
		{
			return Convert.FromHexString(signatureHeader);
		}
	}

	private static bool VerifyEd25519Signature(byte[] signatureBytes, byte[] payloadBytes, string publicKeyHex)
	{
		var publicKey = new Ed25519PublicKeyParameters(Convert.FromHexString(publicKeyHex));
		var verifier = new Ed25519Signer();
		verifier.Init(false, publicKey);
		verifier.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
		return verifier.VerifySignature(signatureBytes);
	}
}
