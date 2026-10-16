#!/bin/bash
# WO-190 (never shipped): run190.py's scenarios in a row, one mode or both. Needs W190_SP, KCD2MP_INSTALL and DOTNET_ROOT in the environment.
#   MODES="before after" SCENS="figwalk copywalk ..." ./batch190.sh
: "${W190_SP:?set W190_SP to the session rig folder}"
: "${KCD2MP_INSTALL:?set KCD2MP_INSTALL to the Modding Tools folder}"
R="$W190_SP/results"
for mode in ${MODES:-before after}; do
  for s in ${SCENS:-figwalk copywalk figfight fighits fighitsarmed copyfight copyhits copyduel opp clinch town townwalk auto}; do
    timeout 600 python3 run190.py $s $mode > "$R/$s.$mode.out" 2>&1
    echo "$(date +%H:%M:%S) $s $mode exit=$?"
  done
done
echo BATCH-DONE
