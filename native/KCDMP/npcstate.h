// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-143: the NPC-state pieces WO-141 anchors (wo141.cpp), for WO-143's hand
// content, gaits, one-shots and look targets (wo143.cpp). Every call is
// SEH-guarded and fails closed exactly as it does inside WO-141; main thread.

#include "wo143_rules.h"

#include <windows.h>
#include <cstddef>
#include <cstdint>
#include <string>

namespace kcdmp::wo141::x {

HMODULE xgenai();
void* npc_manager();                       // *(XGenAI+0x2E52F08), WO-141's anchor
void* context_of(uint32_t eid);            // the body's C_NPCContext (vftable-checked) or null
void* entity_named(const char* name);
void* entity_of(uint64_t wuid);            // WUID -> IEntity (the WUID service)
uint64_t guid_of(uint64_t wuid);           // WUID -> level entity GUID (0 none)
uint64_t wuid_of(uint64_t guid);           // level entity GUID -> this machine's WUID
bool state_element(void* state, unsigned slot, void** el);   // a state's fixed slot (no reference taken)
bool bind_rttr(const char* registeredName, void** typeData);
bool create_element(void* typeData, void* const* vft, void** p, void** ctrl);   // one reference for the caller
void release(void* ctrl);
bool apply_armed();
void* loaded_state(void* ctx);             // ctx+0x180, checked C_NPCRequiredState
void* current_state(void* ctx);            // ctx+0x90
bool clear_loaded(void* loaded);           // its own Clear (slot 0x38)
bool set_loaded_slot(void* loaded, unsigned slot, void* p, void* ctrl);   // hands the reference over
bool execute_loaded(void* ctx, uint8_t* res);   // C_NPCContext::ExecuteStateChangeIntoLoadedState
std::string describe_state(void* state);
const char* unstance_label(uint16_t id, char* buf, size_t n);
int unstance_index(const char* name);

// Hand content (WO-143 Phase 1), carried by WO-141's apply and reconcile.
bool hands_armed();
bool apply_on();                                   // WO-141's apply is on (mp_activities, a session)
bool read_body_hands(uint32_t eid, wo143rules::Hands* out);
std::string hand_classes_text(const wo143rules::Hands& h);
std::string class_id_text(const wo143rules::ClassId& c);
void set_hands(const std::string& name, const wo143rules::Hands& h);
int clear_all_hands();
bool desired_hands(const std::string& name, wo143rules::Hands* out);
void set_need_item_callback(void (*fn)(const char* name, const wo143rules::ClassId& cls));
// Right before a one-shot's request: the body's search state holds what it must
// keep (WO-141's wanted activity and tools, else its own current ones).
bool prepare_request(uint32_t eid, std::string* kept);
void set_hand_applied_callback(void (*fn)(uint32_t eid));
bool retake_hands(const std::string& name);   // research

} // namespace kcdmp::wo141::x
