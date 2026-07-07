// TSLib - A free TeamSpeak 3 and 5 client library
// Copyright (C) 2017  TSLib contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the Open Software License v. 3.0
//
// You should have received a copy of the Open Software License along with this
// program. If not, see <https://opensource.org/licenses/OSL-3.0>.

using System;

namespace TSLib.Helper;

public static class SpanExtensions
{
	public static string NewUtf8String(this ReadOnlySpan<byte> span) => Tools.Utf8Encoder.GetString(span);

	public static string NewUtf8String(this Span<byte> span) => ((ReadOnlySpan<byte>)span).NewUtf8String();
}
