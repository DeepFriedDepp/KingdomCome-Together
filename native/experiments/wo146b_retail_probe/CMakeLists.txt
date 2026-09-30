# WO-146B research probe -- a standalone project, on purpose.
#
# This is NOT part of the shipped product and is deliberately not added to
# native/CMakeLists.txt: nothing links it into KCDMP.dll, the installer never
# sees it, and it is meant to be built by hand for one measuring session
# (build.cmd does that without CMake at all). It is kept here so the sources
# that produced docs/WO-146B-findings.md are in the tree.
#
#   cmake -S native/experiments/wo146b_retail_probe -B native/build/wo146b-cmake
#   cmake --build native/build/wo146b-cmake --config Release
cmake_minimum_required(VERSION 3.21)
project(WO146BRetailProbe CXX)

if(NOT MSVC)
    message(FATAL_ERROR "Must be built with MSVC: this runs inside an MSVC x64 game process.")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded")

add_library(WO146BRetailProbe SHARED probe.cpp probe_pe.cpp probe_hook.cpp)
target_link_libraries(WO146BRetailProbe PRIVATE bcrypt)
# Keep the build machine's folder layout out of the binary (native/CMakeLists.txt
# does the same for the shipped targets).
target_link_options(WO146BRetailProbe PRIVATE "/PDBALTPATH:%_PDB%")
