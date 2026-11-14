// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// A copy of a shopkeeper keeps the host keeper's shop (shops.h).
#include "shops.h"
#include "wo221.h"
#include "wo215.h"
#include "wo203.h"
#include "wo163.h"
#include "anchors.h"
#include "engine.h"
#include "fault_guard.h"
#include "log.h"
#include "npc_drive.h"
#include "respawn_actions.h"

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <atomic>
#include <cctype>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>

namespace kcdmp::shops {

namespace {

// ---- anchors (this build's ShopModule; each is checked by bytes at install, none is a fixed address) ------------------------------
// the game interface (+0x118) -> the shop module's holder (+0x10) -> the shop list; the list's own std::list of shops at +0x10, a
// node's shop at +0x18; C_Shop: database id (int) +0x1B0, its keepers (a std::list of ids) +0x190, their count +0x198.
// C_Shop's primary vftable: slot 5 (+0x28) IsKeeper(const id*), slot 16 (+0x80) Open(const id*) -- the keeper is added and a shop
// that had none is restocked as its own "restock on open" flag says --, slot 17 (+0x88) Close(const id*).
constexpr size_t kGiShop = 0x118, kHolderList = 0x10, kListHead = 0x10, kNodeShop = 0x18;
constexpr size_t kShopDbId = 0x1B0, kShopKeepers = 0x190;
constexpr size_t kSlotIsKeeper = 5, kSlotOpen = 16, kSlotClose = 17;

std::atomic<bool> g_armed{false};
void* const* g_vftShop = nullptr;
std::string g_why = "not installed";
std::atomic<uint32_t> c_opened{0}, c_already{0}, c_closed{0}, c_noEntity{0}, c_noShop{0}, c_onMain{0};

std::mutex g_mu;
std::unordered_map<std::string, int> g_ours;   // the copies this module made keepers: lower-case name -> shop db id

template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "shops::rd");
    if (!base || !fault::plausible_address(reinterpret_cast<uintptr_t>(base) + off)) return false;
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}

std::string lower(const char* s) {
    std::string o(s ? s : "");
    for (auto& c : o) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return o;
}

// The shop with this database id, or null. Every pointer on the way is read under the guard; a shop is one only with C_Shop's own
// vftable.
void* shop_by_id(int dbId) {
    void* gi = engine::game_iface();
    void* holder = nullptr; void* list = nullptr; void* head = nullptr; void* node = nullptr;
    if (!gi || !rd(gi, kGiShop, &holder) || !holder || !rd(holder, kHolderList, &list) || !list) return nullptr;
    if (!rd(list, kListHead, &head) || !head || !rd(head, 0, &node)) return nullptr;
    for (int i = 0; node && node != head && i < 4096; ++i) {
        void* shop = nullptr; void* vp = nullptr; int id = -1;
        if (!rd(node, kNodeShop, &shop) || !shop) return nullptr;
        if (rd(shop, 0, &vp) && vp == static_cast<const void*>(g_vftShop) && rd(shop, kShopDbId, &id) && id == dbId) return shop;
        if (!rd(node, 0, &node)) return nullptr;
    }
    return nullptr;
}

// The three calls are the engine's own on its own object, made where the engine makes them (the AI update's thread). IsKeeper only
// reads; Open and Close may restock or log through the game's own services, so they are plain calls -- never under the fault guard
// (a fault caught while the game held one of its locks would leave it locked).
bool is_keeper(void* shop, const uint64_t* id) {
    auto fn = reinterpret_cast<bool (*)(void*, const uint64_t*)>(const_cast<void*>(g_vftShop[kSlotIsKeeper]));
    return fn(shop, id);
}
void open_shop(void* shop, const uint64_t* id) { reinterpret_cast<void (*)(void*, const uint64_t*)>(const_cast<void*>(g_vftShop[kSlotOpen]))(shop, id); }
void close_shop(void* shop, const uint64_t* id) { reinterpret_cast<void (*)(void*, const uint64_t*)>(const_cast<void*>(g_vftShop[kSlotClose]))(shop, id); }

struct Job { bool open; int dbId; uint64_t wuid; std::string name; };

// At the AI update's entry (or, with no door, on the main thread): the shop looked up again, then the engine's call.
void run(const Job& j) {
    void* shop = shop_by_id(j.dbId);
    if (!shop) { c_noShop.fetch_add(1, std::memory_order_relaxed); return; }
    const std::string key = lower(j.name.c_str());
    if (j.open) {
        if (is_keeper(shop, &j.wuid)) { c_already.fetch_add(1, std::memory_order_relaxed); return; }
        // the keepers the shop had (their ids, for the record: a copy's id is its soul's, the same kind the game's own keepers carry)
        char had[120] = "";
        {
            void* head = nullptr; void* node = nullptr; int m = 0; size_t o = 0;
            if (rd(shop, kShopKeepers, &head) && head && rd(head, 0, &node))
                for (; node && node != head && m < 4; ++m) {
                    uint64_t w = 0;
                    if (!rd(node, 0x10, &w)) break;
                    o += static_cast<size_t>(std::snprintf(had + o, sizeof had - o, "%s%016llX", m ? "," : "", static_cast<unsigned long long>(w)));
                    if (!rd(node, 0, &node) || o >= sizeof had - 20) break;
                }
        }
        open_shop(shop, &j.wuid);
        { std::lock_guard<std::mutex> l(g_mu); g_ours[key] = j.dbId; }
        const uint32_t k = c_opened.fetch_add(1, std::memory_order_relaxed) + 1;
        if (k <= 200 || k % 100 == 0)
            logf("WO221-SHOP npc=%s (id %016llX) is a keeper of shop %d here now, as the host's keeper is (the shop's own Open; its keepers before: [%s]) [n=%u]",
                 j.name.c_str(), static_cast<unsigned long long>(j.wuid), j.dbId, had, k);
    } else {
        if (is_keeper(shop, &j.wuid)) close_shop(shop, &j.wuid);
        { std::lock_guard<std::mutex> l(g_mu); g_ours.erase(key); }
        const uint32_t k = c_closed.fetch_add(1, std::memory_order_relaxed) + 1;
        if (k <= 200 || k % 100 == 0) logf("WO221-SHOP npc=%s keeps shop %d no longer (the host's keeper does not): the shop's own Close [n=%u]", j.name.c_str(), j.dbId, k);
    }
}

void post(Job j) {
    if (!wo215::ai_post([j] { run(j); })) { c_onMain.fetch_add(1, std::memory_order_relaxed); run(j); }
}

uint8_t op(uint8_t opc, const uint8_t* body, size_t len, uint8_t* out, size_t cap, size_t* outLen) {
    *outLen = 0;
    if (cap < 1) return wo163::kRBadRequest;
    auto answer = [&](uint8_t code) { out[0] = code; *outLen = 1; return wo163::kROk; };
    if (!g_armed.load()) return answer(kNotArmed);
    char name[64]{};
    int dbId = -1;
    if (opc == kOpOpen) {
        if (len < 7) return wo163::kRBadRequest;
        std::memcpy(&dbId, body + 1, 4);
        const uint8_t nl = body[5];
        if (nl == 0 || nl > 63 || static_cast<size_t>(6 + nl) != len) return wo163::kRBadRequest;
        std::memcpy(name, body + 6, nl);
    } else if (opc == kOpClose) {
        if (len < 3) return wo163::kRBadRequest;
        const uint8_t nl = body[1];
        if (nl == 0 || nl > 63 || static_cast<size_t>(2 + nl) != len) return wo163::kRBadRequest;
        std::memcpy(name, body + 2, nl);
        std::lock_guard<std::mutex> l(g_mu);
        const auto it = g_ours.find(lower(name));
        if (it == g_ours.end()) return answer(kNotOurs);
        dbId = it->second;
    } else return wo163::kRBadRequest;
    void* e = npcdrive::entity_by_name(name);
    const uint64_t wuid = e ? actions::entity_wuid(e) : 0;
    if (!e || !wuid) {
        c_noEntity.fetch_add(1, std::memory_order_relaxed);
        if (opc == kOpClose) { std::lock_guard<std::mutex> l(g_mu); g_ours.erase(lower(name)); }   // the body is gone: nothing left to close
        return answer(kNoEntity);
    }
    if (opc == kOpOpen) {
        void* shop = shop_by_id(dbId);
        if (!shop) { c_noShop.fetch_add(1, std::memory_order_relaxed); return answer(kNoShop); }
        // is it a keeper already? (read here for the answer: the keeper list is walked under the guard, never called)
        void* head = nullptr; void* node = nullptr;
        bool keeper = false;
        if (rd(shop, kShopKeepers, &head) && head && rd(head, 0, &node)) {
            for (int i = 0; node && node != head && i < 256; ++i) {
                uint64_t w = 0;
                if (rd(node, 0x10, &w) && w == wuid) { keeper = true; break; }
                if (!rd(node, 0, &node)) break;
            }
        }
        if (keeper) { c_already.fetch_add(1, std::memory_order_relaxed); return answer(kAlreadyKeeper); }
    }
    post(Job{opc == kOpOpen, dbId, wuid, name});
    return answer(kQueued);
}

void fail(const char* why) { g_why = why; logf("WO221-SHOP NOT ARMED: %s -- a copy keeps no shop here unless the host's save had it open (0.65.0's behaviour)", why); }

void install() {
    HMODULE sm = GetModuleHandleA("ShopModule.dll");
    if (!sm) { fail("ShopModule.dll not loaded"); return; }
    g_vftShop = anchor::find_vftable(sm, ".?AVC_Shop@shopmodule@wh@@", 0);
    if (!g_vftShop) { fail("C_Shop's vftable not found by RTTI"); return; }
    auto has = [&](const void* fn, std::initializer_list<uint8_t> pat) { return anchor::function_has_bytes(sm, fn, pat.begin(), pat.size()); };
    // Open: the keepers' count read (+0x198), the add through the shop's own slot 6 (+0x30)
    const bool open = has(g_vftShop[kSlotOpen], {0x48, 0x8B, 0x9E, 0x98, 0x01, 0x00, 0x00}) && has(g_vftShop[kSlotOpen], {0xFF, 0x50, 0x30});
    // IsKeeper: the keepers' list (+0x190) walked, a node's id at +0x10 compared
    const bool isk = has(g_vftShop[kSlotIsKeeper], {0x48, 0x8B, 0x8B, 0x90, 0x01, 0x00, 0x00}) && has(g_vftShop[kSlotIsKeeper], {0x4C, 0x39, 0x40, 0x10});
    // Close: the same list
    const bool close = has(g_vftShop[kSlotClose], {0x48, 0x8B, 0x9D, 0x90, 0x01, 0x00, 0x00});
    if (!open || !isk || !close) { fail("C_Shop's Open / IsKeeper / Close are not the functions read in this build"); return; }
    // the list's own walk by database id: a node's shop at +0x18, the id at +0x1B0 (found once in the module's code)
    anchor::Range text{};
    static const uint8_t walk[] = {0x48, 0x8B, 0x48, 0x18, 0x39, 0xB9, 0xB0, 0x01, 0x00, 0x00};   // mov rcx,[rax+18h]; cmp [rcx+1B0h],edi
    int n = 0;
    if (anchor::section(sm, ".text", &text))
        for (const uint8_t* p = text.begin; p + sizeof walk <= text.end; ++p)
            if (std::memcmp(p, walk, sizeof walk) == 0 && ++n > 1) break;
    if (n != 1) { fail("the shop list's walk by database id was not found once"); return; }
    g_armed = true; g_why = "armed";
    logf("WO221-SHOP armed: C_Shop's Open, IsKeeper and Close found by RTTI and checked by bytes; the shop list read as the game's own walk reads it");
}

int status(char* out, int n) {
    size_t ours = 0;
    { std::lock_guard<std::mutex> l(g_mu); ours = g_ours.size(); }
    return std::snprintf(out, static_cast<size_t>(n), "armed=%d (%s) keepers_here=%zu opened=%u already=%u closed=%u no_entity=%u no_shop=%u on_main=%u",
                         g_armed.load() ? 1 : 0, g_why.c_str(), ours, c_opened.load(), c_already.load(), c_closed.load(), c_noEntity.load(), c_noShop.load(),
                         c_onMain.load());
}

const bool kReg = wo203::reg_install("shops", &install) && wo203::reg_op(kOpOpen, &op) && wo203::reg_op(kOpClose, &op) && wo221::add_status("shops", &status);

} // namespace

bool armed() { return g_armed.load(); }

} // namespace kcdmp::shops
