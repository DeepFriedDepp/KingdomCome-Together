-- generated: every derived-stat code of the build, read for the player (WO-195B probe)
function W195B_All0()
  local o = {}
  for _, c in ipairs({"bard","rch","cse","vib","evi","dai","bnc","hac","hde","dtf","dbf","sdt","fov","vir","tsr","con","mst","mhs","hlt","nsb","rsa","ble","ibi","noi","fsm","nrs","rsb","oad","owa","wud","lvl","lfu","lio","cap","alo","mos","ore","caf","rdq","obe","psr","xpm","osl","rcw","mor","dem","mcf","bow","cow","aco","caw","iex","hcm","was","grm","cag","def","dru","ran","mut","prb","fpa","apa","erq","sle","coc","ufo","sha","pbm","rml","hml","sma","bma","hgs","pds"}) do
    local v = nil
    pcall(function() v = player.soul:GetDerivedStat(c) end)
    o[#o + 1] = c .. "=" .. string.format("%.3f", tonumber(v) or -99)
  end
  System.LogAlways("[W195B] all0 " .. table.concat(o, " "))
end
function W195B_All1()
  local o = {}
  for _, c in ipairs({"frg","hko","enc","fdm","brm","drt","sml","smi","kopr","cds","edm","prc","ppr","ltp","upr","skpr","jrm","sur","imm","apr","bso","bld","bba","arr","rms","deb","ach","prs","alm","hov","btw","owl","cbi","nbi","alc","adm","cdw","eqw","pla","wbc","sdn","brn","plr","bea","aag","red","drv","pmc","bad","nrv","vag","mag","imp","drd","dmt","mgt","prm","uvh","uva","kko","sse","ies","lrm","dfe","csc","hvp","pqe","slp","cep","icp","crp","cnp","cbp","csp","xma"}) do
    local v = nil
    pcall(function() v = player.soul:GetDerivedStat(c) end)
    o[#o + 1] = c .. "=" .. string.format("%.3f", tonumber(v) or -99)
  end
  System.LogAlways("[W195B] all1 " .. table.concat(o, " "))
end
function W195B_All2()
  local o = {}
  for _, c in ipairs({"xsc","srr","bap","xsw","xcr","xsu","xhw","xag","xvi","xsp","pxp","sks","sst","sss","xst","eph","rpd","rpr","rpa","ahe","apo","ano","fpd","lfb","tba","nrw","fac","alp","atd","ams","rcx","fba","lip","sxm","fvh","atp","hlm","dip","hcd","bmr","lgp","bbn","shd","hla","rpp","efd","aml","plg","ade","dig","exh","hod","psp","cli","hlh","slh","sls","rtm","act","asp","pbs","uat","wat","wac","ahm","ain","hin","fae","dee","pdp","eep","lpd","lpn","sra","ard"}) do
    local v = nil
    pcall(function() v = player.soul:GetDerivedStat(c) end)
    o[#o + 1] = c .. "=" .. string.format("%.3f", tonumber(v) or -99)
  end
  System.LogAlways("[W195B] all2 " .. table.concat(o, " "))
end
function W195B_All3()
  local o = {}
  for _, c in ipairs({"skp","rst","lcs","srg","sco","srb","ald","aam","uab","fhm","ptp","shr","ser","pim","pac","lpb","res","ors","dew","osb","dsl","cos","efc","dsc","wde","ewd","wsr","war","wrs","wra","adc","sla","sta","ssa","fne","dth","dtd","tdh","ode","dmd","dmh","drn","ala","apq","bid","fob","acm","rat","rls","ptw","scv","scx","skv","cat","aps","fbm","tdm","cps","phv","bnv","pnv","fnv","paa","itd","rkm","rhm","bqc","bqy","ifc","pwc","btd","opd","spv","pct","nhp"}) do
    local v = nil
    pcall(function() v = player.soul:GetDerivedStat(c) end)
    o[#o + 1] = c .. "=" .. string.format("%.3f", tonumber(v) or -99)
  end
  System.LogAlways("[W195B] all3 " .. table.concat(o, " "))
end
function W195B_All4()
  local o = {}
  for _, c in ipairs({"nrb"}) do
    local v = nil
    pcall(function() v = player.soul:GetDerivedStat(c) end)
    o[#o + 1] = c .. "=" .. string.format("%.3f", tonumber(v) or -99)
  end
  System.LogAlways("[W195B] all4 " .. table.concat(o, " "))
end
