// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// Portions from the original project, marczukmichal/kcd2-multiplayer; its author keeps their copyright (AUTHORS).
namespace KcdMp.Server.Features.ServerInformation.Dtos;

/// <summary>
/// Server info DTO.
/// </summary>
public record ServerInfo
{
	/// <summary>
	/// The map's name (Trosky or Kuttenberg)
	/// </summary>
	public string MapName { get; set; } = string.Empty;
	
	/// <summary>
	/// The current player count.
	/// </summary>
	public int Players { get; set; }
	
	/// <summary>
	/// The max player count.
	/// </summary>
	public int MaxPlayers { get; set; }
	
	/// <summary>
	/// The server's tags.
	/// </summary>
	public string[] Tags { get; set; } = [];
}