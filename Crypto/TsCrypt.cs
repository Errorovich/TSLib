// TSLib - A free TeamSpeak 3 and 5 client library
// Copyright (C) 2017  TSLib contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Security;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TSLib.Helper;
using TSLib.Shared;

namespace TSLib.Crypto;

/// <summary>Статические крипто-утилиты протокола TeamSpeak: импорт/экспорт и генерация identity,
/// уровень безопасности, хеши, подписи. Per-connection пакетный шифр — <c>TSLib.Full.Transport.PacketCipher</c>.</summary>
public static class TsCrypt
{
	#region KEY IMPORT/EXPROT

	/// <summary>
	/// Detects the kind of key and creates an identity from it.
	/// This method can import 3 kinds of identity keys.
	/// <list type="bullet">
	/// <item><description>The Teamspeak 3 key as it is stored by the normal client.</description></item>
	/// <item><description>A libtomcrypt public+private key export. (+KeyOffset).</description></item>
	/// <item><description>A TSLib private-only key export. (+KeyOffset).</description></item>
	/// </list>
	/// Keys with "(+KeyOffset)" should add the key offset for the security level in the separate parameter.
	/// </summary>
	/// <param name="key">The identity string.</param>
	/// <param name="keyOffset">A number which determines the security level of an identity.</param>
	/// <param name="lastCheckedKeyOffset">The last brute forced number. Default 0: will take the current keyOffset.</param>
	/// <returns>The identity information.</returns>
	public static R<IdentityData, string> LoadIdentityDynamic(string key, ulong keyOffset = 0, ulong lastCheckedKeyOffset = 0)
	{
		var tsIdentity = DeobfuscateAndImportTsIdentity(key);
		if (tsIdentity.Ok)
			return tsIdentity.Value;
		return LoadIdentity(key, keyOffset, lastCheckedKeyOffset);
	}

	/// <summary>This methods loads a secret identity.</summary>
	/// <param name="key">The key stored in base64, encoded like the libtomcrypt export method of a private key.
	/// Or the TSLib's shorted private-only key.</param>
	/// <param name="keyOffset">A number which determines the security level of an identity.</param>
	/// <param name="lastCheckedKeyOffset">The last brute forced number. Default 0: will take the current keyOffset.</param>
	/// <returns>The identity information.</returns>
	public static R<IdentityData, string> LoadIdentity(string key, ulong keyOffset, ulong lastCheckedKeyOffset = 0)
	{
		var asnByteArray = Base64Decode(key);
		if (asnByteArray is null)
			return "Invalid identity base64 string";
		var importRes = ImportKeyDynamic(asnByteArray);
		if (!importRes.Ok)
			return importRes.Error;
		var (publicKey, privateKey) = importRes.Value;
		if (privateKey is null)
			return "Key string did not contain a private key";
		return LoadIdentity(publicKey, privateKey, keyOffset, lastCheckedKeyOffset);
	}

	private static IdentityData LoadIdentity(ECPoint? publicKey, BigInteger privateKey, ulong keyOffset, ulong lastCheckedKeyOffset)
	{
		return new IdentityData(privateKey, publicKey)
		{
			ValidKeyOffset = keyOffset,
			LastCheckedKeyOffset = lastCheckedKeyOffset < keyOffset ? keyOffset : lastCheckedKeyOffset,
		};
	}

	private static readonly ECKeyGenerationParameters KeyGenParams = new ECKeyGenerationParameters(X9ObjectIdentifiers.Prime256v1, new SecureRandom());

	internal static R<ECPoint, string> ImportPublicKey(byte[] asnByteArray)
	{
		try
		{
			var asnKeyData = (DerSequence)Asn1Object.FromByteArray(asnByteArray);
			var x = ((DerInteger)asnKeyData[2]).Value;
			var y = ((DerInteger)asnKeyData[3]).Value;

			var ecPoint = KeyGenParams.DomainParameters.Curve.CreatePoint(x, y);
			return ecPoint;
		}
		catch (Exception) { return "Could not import public key"; }
	}

	private static R<(ECPoint? publicKey, BigInteger? privateKey), string> ImportKeyDynamic(byte[] asnByteArray)
	{
		BigInteger? privateKey = null;
		ECPoint? publicKey = null;
		try
		{
			var asnKeyData = (DerSequence)Asn1Object.FromByteArray(asnByteArray);
			var bitInfo = ((DerBitString)asnKeyData[0]).IntValue;
			if (bitInfo == 0b0000_0000 || bitInfo == 0b1000_0000)
			{
				var x = ((DerInteger)asnKeyData[2]).Value;
				var y = ((DerInteger)asnKeyData[3]).Value;
				publicKey = KeyGenParams.DomainParameters.Curve.CreatePoint(x, y);

				if (bitInfo == 0b1000_0000)
				{
					privateKey = ((DerInteger)asnKeyData[4]).Value;
				}
			}
			else if (bitInfo == 0b1100_0000)
			{
				privateKey = ((DerInteger)asnKeyData[2]).Value;
			}
		}
		catch (Exception ex) { return $"Could not import identity: {ex.Message}"; }
		return (publicKey, privateKey);
	}

	internal static string ExportPublicKey(ECPoint publicKey)
	{
		var dataArray = new DerSequence(
			new DerBitString(new byte[] { 0b0000_0000 }, 7),
			new DerInteger(32),
			new DerInteger(publicKey.AffineXCoord.ToBigInteger()),
			new DerInteger(publicKey.AffineYCoord.ToBigInteger())).GetDerEncoded();
		return Convert.ToBase64String(dataArray);
	}

	internal static string ExportPrivateKey(BigInteger privateKey)
	{
		var dataArray = new DerSequence(
			new DerBitString(new byte[] { 0b1100_0000 }, 6),
			new DerInteger(32),
			new DerInteger(privateKey)).GetDerEncoded();
		return Convert.ToBase64String(dataArray);
	}

	internal static string ExportPublicAndPrivateKey(ECPoint publicKey, BigInteger privateKey)
	{
		var dataArray = new DerSequence(
			new DerBitString(new byte[] { 0b1000_0000 }, 7),
			new DerInteger(32),
			new DerInteger(publicKey.AffineXCoord.ToBigInteger()),
			new DerInteger(publicKey.AffineYCoord.ToBigInteger()),
			new DerInteger(privateKey)).GetDerEncoded();
		return Convert.ToBase64String(dataArray);
	}

	internal static string GetUidFromPublicKey(string publicKey)
	{
		var publicKeyBytes = Encoding.ASCII.GetBytes(publicKey);
		var hashBytes = Hash1It(publicKeyBytes);
		return Convert.ToBase64String(hashBytes);
	}

	internal static ECPoint RestorePublicFromPrivateKey(BigInteger privateKey)
	{
		var curve = ECNamedCurveTable.GetByOid(X9ObjectIdentifiers.Prime256v1);
		return curve.G.Multiply(privateKey).Normalize();
	}

	private static readonly Regex IdentityRegex = new Regex(@"^(?<level>\d+)V(?<identity>[\w\/\+]+={0,2})$", RegexOptions.ECMAScript | RegexOptions.CultureInvariant);
	private static readonly byte[] TsIdentityObfuscationKey = Encoding.ASCII.GetBytes("b9dfaa7bee6ac57ac7b65f1094a1c155e747327bc2fe5d51c512023fe54a280201004e90ad1daaae1075d53b7d571c30e063b5a62a4a017bb394833aa0983e6e");

	public static R<IdentityData, string> DeobfuscateAndImportTsIdentity(string identity)
	{
		var match = IdentityRegex.Match(identity);
		if (!match.Success)
			return "Identity could not get matched as teamspeak identity";

		if (!ulong.TryParse(match.Groups["level"].Value, out var level))
			return "Invalid key offset";

		var ident = Base64Decode(match.Groups["identity"].Value);
		if (ident is null)
			return "Invalid identity base64 string";

		if (ident.Length < 20)
			return "Identity too short";

		int nullIdx = ident.AsSpan(20).IndexOf((byte)0);
		var hash = Hash1It(ident, 20, nullIdx < 0 ? ident.Length - 20 : nullIdx);

		XorBinary(ident, hash, 20, ident);
		XorBinary(ident, TsIdentityObfuscationKey, Math.Min(100, ident.Length), ident);

		if (System.Buffers.Text.Base64.DecodeFromUtf8InPlace(ident, out var length) != System.Buffers.OperationStatus.Done)
			return "Invalid deobfuscated base64 string";

		var importRes = ImportKeyDynamic(ident.AsSpan(0, length).ToArray());
		if (!importRes.Ok)
			return importRes.Error;

		var (publicKey, privateKey) = importRes.Value;
		if (privateKey is null)
			return "Key string did not contain a private key";
		return LoadIdentity(publicKey, privateKey, level, level);
	}

	#endregion

	#region CRYPT HELPER

	internal static void XorBinary(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int len, Span<byte> outBuf)
	{
		if (a.Length < len || b.Length < len || outBuf.Length < len) throw new ArgumentException();
		for (int i = 0; i < len; i++)
			outBuf[i] = (byte)(a[i] ^ b[i]);
	}

	internal static byte[] Hash1It(byte[] data, int offset = 0, int len = 0) => System.Security.Cryptography.SHA1.HashData(HashSpan(data, offset, len));
	internal static byte[] Hash256It(byte[] data, int offset = 0, int len = 0) => System.Security.Cryptography.SHA256.HashData(HashSpan(data, offset, len));
	internal static byte[] Hash512It(byte[] data, int offset = 0, int len = 0) => System.Security.Cryptography.SHA512.HashData(HashSpan(data, offset, len));
	private static ReadOnlySpan<byte> HashSpan(byte[] data, int offset, int len) => data.AsSpan(offset, len == 0 ? data.Length - offset : len);

	/// <summary>
	/// Hashes a password like TeamSpeak.
	/// The hash works like this: base64(sha1(password))
	/// </summary>
	/// <param name="password">The password to hash.</param>
	/// <returns>The hashed password.</returns>
	public static string HashPassword(string password)
	{
		if (string.IsNullOrEmpty(password))
			return string.Empty;
		var bytes = Tools.Utf8Encoder.GetBytes(password);
		var hashed = Hash1It(bytes);
		return Convert.ToBase64String(hashed);
	}

	public static byte[] Sign(BigInteger privateKey, byte[] data)
	{
		var signer = SignerUtilities.GetSigner(X9ObjectIdentifiers.ECDsaWithSha256);
		var signKey = new ECPrivateKeyParameters(privateKey, KeyGenParams.DomainParameters);
		signer.Init(true, signKey);
		signer.BlockUpdate(data, 0, data.Length);
		return signer.GenerateSignature();
	}

	public static bool VerifySign(ECPoint publicKey, byte[] data, byte[] proof)
	{
		var signer = SignerUtilities.GetSigner(X9ObjectIdentifiers.ECDsaWithSha256);
		var signKey = new ECPublicKeyParameters(publicKey, KeyGenParams.DomainParameters);
		signer.Init(false, signKey);
		signer.BlockUpdate(data, 0, data.Length);
		return signer.VerifySignature(proof);
	}

	private static readonly byte[] TsVersionSignPublicKey = Convert.FromBase64String("UrN1jX0dBE1vulTNLCoYwrVpfITyo+NBuq/twbf9hLw=");

	public static bool EdCheck(TsVersionSigned sign)
	{
		var ver = Encoding.ASCII.GetBytes(sign.Platform + sign.Version);
		var signArr = Base64Decode(sign.Sign);
		if (signArr is null)
			return false;
		return Chaos.NaCl.Ed25519.Verify(signArr, ver, TsVersionSignPublicKey);
	}

	public static void VersionSelfCheck()
	{
		var versions = typeof(TsVersionSigned).GetProperties().Where(prop => prop.PropertyType == typeof(TsVersionSigned));
		foreach (var ver in versions)
		{
			var verObj = (TsVersionSigned)ver.GetValue(null)!;
			if (!EdCheck(verObj))
				throw new Exception($"Version is invalid: {verObj}");
		}
	}

	internal static byte[]? Base64Decode(string str)
	{
		try { return Convert.FromBase64String(str); }
		catch (FormatException) { return null; }
	}

	#endregion

	#region IDENTITY & SECURITY LEVEL

	/// <summary>Equals ulong.MaxValue.ToString().Length</summary>
	private const int MaxUlongStringLen = 20;

	/// <summary><para>Tries to improve the security level of the provided identity to the new level.</para>
	/// <para>The algorithm takes approximately 2^toLevel milliseconds to calculate; so be careful!</para>
	/// This method can be canceled anytime since progress which is not enough for the next level
	/// will be saved in <see cref="IdentityData.LastCheckedKeyOffset"/> continuously.</summary>
	/// <param name="identity">The identity to improve.</param>
	/// <param name="toLevel">The targeted level.</param>
	public static void ImproveSecurity(IdentityData identity, int toLevel)
	{
		var hashBuffer = new byte[identity.PublicKeyString.Length + MaxUlongStringLen];
		var pubKeyBytes = Encoding.ASCII.GetBytes(identity.PublicKeyString);
		Array.Copy(pubKeyBytes, 0, hashBuffer, 0, pubKeyBytes.Length);

		identity.LastCheckedKeyOffset = Math.Max(identity.ValidKeyOffset, identity.LastCheckedKeyOffset);
		int best = GetSecurityLevel(hashBuffer, pubKeyBytes.Length, identity.ValidKeyOffset);
		while (true)
		{
			if (best >= toLevel) return;

			int curr = GetSecurityLevel(hashBuffer, pubKeyBytes.Length, identity.LastCheckedKeyOffset);
			if (curr > best)
			{
				identity.ValidKeyOffset = identity.LastCheckedKeyOffset;
				best = curr;
			}
			identity.LastCheckedKeyOffset++;
		}
	}

	public static int GetSecurityLevel(IdentityData identity)
	{
		var hashBuffer = new byte[identity.PublicKeyString.Length + MaxUlongStringLen];
		var pubKeyBytes = Encoding.ASCII.GetBytes(identity.PublicKeyString);
		Array.Copy(pubKeyBytes, 0, hashBuffer, 0, pubKeyBytes.Length);
		return GetSecurityLevel(hashBuffer, pubKeyBytes.Length, identity.ValidKeyOffset);
	}

	/// <summary>Creates a new TeamSpeak3 identity.</summary>
	/// <param name="securityLevel">Minimum security level this identity will have.</param>
	/// <returns>The identity information.</returns>
	public static IdentityData GenerateNewIdentity(int securityLevel = 8)
	{
		var ecp = ECNamedCurveTable.GetByName("prime256v1");
		var domainParams = new ECDomainParameters(ecp.Curve, ecp.G, ecp.N, ecp.H, ecp.GetSeed());
		var keyGenParams = new ECKeyGenerationParameters(domainParams, new SecureRandom());
		var generator = new ECKeyPairGenerator();
		generator.Init(keyGenParams);
		var keyPair = generator.GenerateKeyPair();

		var privateKey = (ECPrivateKeyParameters)keyPair.Private;
		var publicKey = (ECPublicKeyParameters)keyPair.Public;

		var identity = LoadIdentity(publicKey.Q.Normalize(), privateKey.D, 0, 0);
		ImproveSecurity(identity, securityLevel);
		return identity;
	}

	private static int GetSecurityLevel(byte[] hashBuffer, int pubKeyLen, ulong offset)
	{
		var numBuffer = new byte[MaxUlongStringLen];
		int numLen = 0;
		do
		{
			numBuffer[numLen] = (byte)('0' + (offset % 10));
			offset /= 10;
			numLen++;
		} while (offset > 0);
		for (int i = 0; i < numLen; i++)
			hashBuffer[pubKeyLen + i] = numBuffer[numLen - (i + 1)];
		byte[] outHash = Hash1It(hashBuffer, 0, pubKeyLen + numLen);

		return GetLeadingZeroBits(outHash);
	}

	private static int GetLeadingZeroBits(byte[] data)
	{
		// TODO dnc 3.0 sse ?
		int curr = 0;
		int i;
		for (i = 0; i < data.Length; i++)
			if (data[i] == 0) curr += 8;
			else break;
		if (i < data.Length)
			for (int bit = 0; bit < 8; bit++)
				if ((data[i] & (1 << bit)) == 0) curr++;
				else break;
		return curr;
	}

	#endregion
}
