// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// A copy of a shopkeeper keeps the host keeper's shop (WO-221 B, mp_shop_keeper; docs/WO-221-findings.md).
//
// In the game alone a shop has keepers only while a keeper's own NPC state holds its "open shop" element: the keeper's brain puts
// it there when it takes up its work (C_OpenShopElement's activation calls the shop's own Open with the keeper's id), and a save
// keeps it. A joiner's copy never takes up its work (its brain is suspended), so every shop that opened after the host's save was
// made has no keeper in the joiner's world: the trade option is there (the host's "ready to sell" context is mirrored since
// WO-139), the dialogue reaches it, and the game ends the conversation with "Failed to find shop from dialog".
//
// The host says which shop each keeper near the players keeps (the game's own script call); here the copy is made that shop's
// keeper by the call the element's activation makes -- C_Shop::Open(keeper), ShopModule, found by RTTI and checked by its bytes --
// and taken off again by C_Shop::Close(keeper) when the host's keeper no longer keeps it. Both run at the AI update's entry on
// its own thread, where the engine activates the element itself. Only what this module opened is ever closed.
//
// Pipe ops (the WO-163 family, registered through wo203.h; numbers 190..199 are WO-221's):
//   192 Open  [shop db id:4][name len:1][name] -> [code:1]
//   193 Close [name len:1][name]               -> [code:1]
#include <cstdint>

namespace kcdmp::shops {

constexpr uint8_t kOpOpen = 192, kOpClose = 193;
// reply codes
constexpr uint8_t kOpened = 1, kAlreadyKeeper = 2, kNoEntity = 3, kNoShop = 4, kNotArmed = 5, kQueued = 6, kClosed = 7, kNotOurs = 8;

bool armed();

} // namespace kcdmp::shops
