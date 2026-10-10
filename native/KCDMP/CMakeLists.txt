# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
add_library(KCDMP SHARED
    dllmain.cpp
    rttr_probe.cpp
    rttr_abi.cpp
    combat_playanim.cpp
    combat_construct.cpp
    script_context.cpp
    main_thread.cpp
    pipe_server.cpp
    dice_hook.cpp
    lua_closure.cpp
    concept_read.cpp
    mannequin_read.cpp
    combat_write.cpp
    local_state.cpp
    npc_scan.cpp
    anchors.cpp
    engine.cpp
    buffs.cpp
    gameover_hook.cpp
    hangover.cpp
    respawn.cpp
    respawn_actions.cpp
    punishment.cpp
    npc_drive.cpp
    npc_trace.cpp
    inline_hook.cpp
    motion.cpp
    hits.cpp
    savelist.cpp
    join_native.cpp
    leash.cpp
    wo131.cpp
    wo132.cpp
    wo135.cpp
    wo136.cpp
    wo137.cpp
    wo138.cpp
    wo139.cpp
    wo140.cpp
    wo141.cpp
    wo143.cpp
    wo147.cpp
    fault_guard.cpp
    wo151.cpp
    wo163.cpp
    wo165.cpp
    wo166.cpp
    weather.cpp
)

target_include_directories(KCDMP PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
set_target_properties(KCDMP PROPERTIES OUTPUT_NAME "KCDMP")
