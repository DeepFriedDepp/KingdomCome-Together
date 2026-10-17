#!/usr/bin/env python3
# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""The in-game cutscene table: every in-game cutscene name -> the quest node that plays it.

For each <IngameCutscene Name=...> row of Libs/Tables/ui/cutscene.xml (Tables.pak) this prints

    name|holder|module|handlerNode|conceptPath

    name         the cutscene's name in the table
    holder       the level entity (EntityClass CutsceneHolder, property esCutsceneName = name) the quest plays it through
    module       the quest module (Skald type) that holds the handler, as the game's log names it:
                 <Database Name>::<Project>::...::<type>, e.g. brambora::Barbora::trosecko::...::cin_s0150t_...
    handlerNode  the CutsceneHandler node's Name inside that module
    conceptPath  the handler's path in the running quest graph: the project, then the Name of the node that
                 instantiates each module in its parent's graph, then handlerNode (dot-separated), e.g.
                 Barbora.trosecko.zbranePanaSemina.h.na_semine.na_semine.cutscenehandler4. A module instantiated
                 more than once gives one line per instance; empty when no instance chain reaches the module.

one line per combination (several holders, handlers or instances for one name -> several lines), sorted by name,
on stdout.
A summary (rows resolved / unresolved and why) goes to stderr.

How the data chains (all read from the paks in memory; nothing is extracted to disk):
  1. Levels/<level>/level.pak (objects_mission0.xml and layers/*.xml): CutsceneHolder entities carry esCutsceneName.
     A quest instance's asset entity carries <Link Name="asset['<alias>']" TargetId="<holder EntityId>"/>.
     The asset entity of a module instance is the entity named after the instance, or the target of a
     <Link Name="module['<instance name>']"/> on an enclosing instance's entity (the level's LevelHolder,
     a random-event place, ...). Assets of the project itself sit on the entity named after the project and on
     each level's LevelHolder.
  2. Scripts.pak Quests/**.xml (Skald): a CutsceneHandler node names its holder with
     <Asset Name="CutsceneHolder" Alias="<alias>"/>. The alias is looked up lexically: the handler's own type, then
     the type that defines it (<Definitions>), and so on up to the project, in each type's <Assets>. Each instance
     chain is resolved on its own: walking up it, the first instance whose asset entity links the alias wins. A
     handler fed through an input port (<Edge From="<port>" To="CutsceneHolder"/>) is followed to the Asset/Edge that
     the instantiating node binds to that port (and through Switch/Select/MakeArray/ElementAt and module outputs).

    python tools/Build-CutsceneHandlers.py [DATA] [--all-projects]

DATA is the game's Data folder (default: %KCD2MP_INSTALL%/Data, else D:/SteamLibrary/steamapps/common/KCD2Mod/Data).
By default only the shipped quest project (Quests/Final) is read; --all-projects adds the test/debug projects.
"""
import argparse, collections, glob, io, os, re, sys, time, zipfile
import xml.etree.ElementTree as ET

DEFAULT_DATA = "D:/SteamLibrary/steamapps/common/KCD2Mod/Data"
ASSET_LINK = re.compile(r"^asset\['(.*)'\]$")
MODULE_LINK = re.compile(r"^module\['(.*)'\]$")


def read_member(z, info):
    """level.pak stores some local-header names with backslashes while the central directory has slashes;
    zipfile refuses the mismatch, so retry with the local spelling."""
    try:
        return z.read(info)
    except zipfile.BadZipFile:
        info.orig_filename = info.filename.replace("/", "\\")
        info.filename = info.orig_filename
        return z.open(info).read()


def find_member(z, path):
    want = path.lower()
    for i in z.infolist():
        if i.filename.replace("\\", "/").lower() == want:
            return i
    return None


# ---------------------------------------------------------------------------------------------------------------- table
def load_cutscene_names(data):
    with zipfile.ZipFile(os.path.join(data, "Tables.pak")) as z:
        info = find_member(z, "Libs/Tables/ui/cutscene.xml")
        if info is None:
            sys.exit("Libs/Tables/ui/cutscene.xml not found in Tables.pak")
        root = ET.fromstring(read_member(z, info))
    return [e.get("Name") for e in root.iter("IngameCutscene") if e.get("Name")]


# --------------------------------------------------------------------------------------------------------------- levels
class Level:
    def __init__(self, name):
        self.name = name
        self.ents = {}                                   # EntityId -> (Name, EntityClass)
        self.by_name = collections.defaultdict(set)      # Name -> {EntityId}
        self.holders = {}                                # EntityId -> esCutsceneName
        self.assets = collections.defaultdict(list)      # source EntityId -> [(alias, target EntityId)]
        self.modules = collections.defaultdict(set)      # instance name -> {target EntityId}
        self.level_holders = set()                       # EntityIds of class LevelHolder


def load_levels(data):
    levels = []
    for pak in sorted(glob.glob(os.path.join(data, "Levels", "*", "level.pak"))):
        lv = Level(os.path.basename(os.path.dirname(pak)))
        with zipfile.ZipFile(pak) as z:
            for info in z.infolist():
                n = info.filename.replace("\\", "/").lower()
                if not n.endswith(".xml") or n.startswith("tables/") or n == "waitinglinks.xml":
                    continue
                d = read_member(z, info)
                if b'EntityClass="CutsceneHolder"' not in d and b"asset[&apos;" not in d and b"module[&apos;" not in d:
                    continue
                for _, e in ET.iterparse(io.BytesIO(d), events=("end",)):
                    if e.tag != "Entity":
                        continue
                    eid, name, cls = e.get("EntityId"), e.get("Name"), e.get("EntityClass")
                    lv.ents[eid] = (name, cls)
                    if cls == "LevelHolder":
                        lv.level_holders.add(eid)
                    lv.by_name[name].add(eid)
                    if cls == "CutsceneHolder":
                        p = e.find("Properties")
                        lv.holders[eid] = p.get("esCutsceneName") if p is not None else None
                    links = e.find("EntityLinks")
                    if links is not None:
                        for l in links.findall("Link"):
                            ln = l.get("Name") or ""
                            m = ASSET_LINK.match(ln)
                            if m:
                                lv.assets[eid].append((m.group(1), l.get("TargetId")))
                                continue
                            m = MODULE_LINK.match(ln)
                            if m:
                                lv.modules[m.group(1)].add(l.get("TargetId"))
                    e.clear()
        levels.append(lv)
    return levels


# ---------------------------------------------------------------------------------------------------------------- skald
class SkaldType:
    def __init__(self, path, kind, name, db, root):
        self.path, self.kind, self.name, self.db, self.root = path, kind, name, db, root
        self.parent = None          # the type whose <Definitions> lists this one
        self.defs = {}              # type name -> SkaldType
        self.defs_lower = {}
        self.assets = set()         # names declared in <Assets>
        self.in_ports = set()
        self.instances = []         # (enclosing SkaldType, instance node Element)
        nodes = root.find("Nodes")
        self.nodes_all = list(nodes) if nodes is not None else []
        self.nodes = {n.get("Name"): n for n in self.nodes_all if n.get("Name")}
        self.node_types = {}        # node Name -> the SkaldType it instantiates


def load_skald(data, all_projects):
    types = {}
    with zipfile.ZipFile(os.path.join(data, "Scripts.pak")) as z:
        for info in z.infolist():
            n = info.filename.replace("\\", "/")
            if not n.lower().endswith(".xml") or not n.startswith("Quests/"):
                continue
            if not all_projects and not n.startswith("Quests/Final/"):
                continue
            d = z.read(info)
            if b"<Skald>" not in d:
                continue
            try:
                root = ET.fromstring(d)
            except ET.ParseError:
                continue
            sk = root.find("Skald")
            if sk is None or len(sk) == 0:
                continue
            top = sk[0]
            t = SkaldType(n, top.tag, top.get("Name") or os.path.basename(n)[:-4], root.get("Name") or "", top)
            a = top.find("Assets")
            if a is not None:
                t.assets = {e.get("Name") for e in a if e.get("Name")}
            ports = top.find("Ports")
            if ports is not None:
                t.in_ports = {p.get("Name") for p in ports.findall("Port") if p.get("Direction") == "In"}
            types[n.lower()] = t
    # definitions -> parent/child
    for t in types.values():
        defs = t.root.find("Definitions")
        if defs is None:
            continue
        base = os.path.dirname(t.path)
        for de in defs.findall("Definition"):
            f = de.get("File")
            c = types.get((base + "/" + f.replace("\\", "/")).lower()) if f else None
            if c is not None:
                c.parent = t
                t.defs[c.name] = c
                t.defs_lower[c.name.lower()] = c
    # instances
    def lookup(scope, tag):
        while scope is not None:
            c = scope.defs.get(tag) or scope.defs_lower.get(tag.lower())
            if c is not None:
                return c
            scope = scope.parent
        return None

    for t in list(types.values()):
        for node in t.nodes_all:
            ns = node.get("Namespace")
            if ns:                       # the namespace's first part is looked up like a tag, the rest below it
                parts = ns.split(".")
                c = lookup(t, parts[0])
                for part in parts[1:] + [node.tag]:
                    c = c and (c.defs.get(part) or c.defs_lower.get(part.lower()))
            else:
                c = lookup(t, node.tag)
            if c is not None:
                c.instances.append((t, node))
                t.node_types[node.get("Name")] = c
    return types


def module_path(t):
    chain = []
    while t is not None:
        chain.append(t.name)
        if t.parent is None:
            break
        t = t.parent
    return "::".join([t.db] + chain[::-1])


# -------------------------------------------------------------------------------------------------------------- resolve
# A context is one concrete instance chain from the project down to a type: ((project, None), (type, node), ...), where
# node is the node that instantiates that type in the previous type's graph. Its node Names make the concept path.
class Resolver:
    MAX_CONTEXTS = 5000

    def __init__(self, levels):
        self.levels = levels
        self.memo = {}
        self.ctx_memo = {}
        self.truncated = set()

    def declaring(self, scope, alias):
        while scope is not None:
            if alias in scope.assets:
                return scope
            scope = scope.parent
        return None

    def entity_hits(self, inst_name, alias, project=False):
        """{(level, holder EntityId)} that asset['alias'] names on the asset entity of an instance called inst_name."""
        out = set()
        for lv in self.levels:
            ents = lv.by_name.get(inst_name, set()) | lv.modules.get(inst_name, set())
            if project:                  # the project's own assets also sit on every level's LevelHolder
                ents = ents | lv.level_holders
            for eid in ents:
                for a, tid in lv.assets.get(eid, ()):
                    if a == alias and tid in lv.holders:
                        out.add((lv.name, tid))
        return out

    # ---- instance chains
    def contexts(self, t):
        """Every instance chain that ends at t (empty when t, or a type above it, is never instantiated)."""
        if t.path in self.ctx_memo:
            return self.ctx_memo[t.path]
        self.ctx_memo[t.path] = []       # cycle guard
        if not t.instances:
            out = [((t, None),)] if t.parent is None else []
        else:
            out = []
            for enc, node in t.instances:
                for c in self.contexts(enc):
                    out.append(c + ((t, node),))
                    if len(out) >= self.MAX_CONTEXTS:
                        self.truncated.add(t.path)
                        break
                if len(out) >= self.MAX_CONTEXTS:
                    break
        self.ctx_memo[t.path] = out
        return out

    @staticmethod
    def lexical_context(t):
        """Stand-in for a type no instance chain reaches: its definition chain, without instance nodes."""
        chain = []
        while t is not None:
            chain.append((t, None))
            t = t.parent
        return tuple(chain[::-1])

    @staticmethod
    def concept_path(ctx, node_name):
        return ".".join([ctx[0][0].name] + [n.get("Name") or "" for _, n in ctx[1:]] + [node_name])

    # ---- holders
    def holders_for(self, t, alias, depth=0):
        """Union over every instance of t (used only for a declaring type that is not on the context's chain)."""
        key = (t.path, alias)
        if key in self.memo:
            return self.memo[key]
        self.memo[key] = set()           # cycle guard
        if not t.instances or depth > 40:
            out = self.entity_hits(t.name, alias, project=t.parent is None)
        else:
            out = set()
            for enc, node in t.instances:
                hit = self.entity_hits(node.get("Name") or "", alias)
                out |= hit if hit else self.holders_for(enc, alias, depth + 1)
        self.memo[key] = out
        return out

    def alias(self, ctx, alias):
        """An alias used in the context's last type -> (holders, reason when none). Walks up the instance chain: the
        first instance whose asset entity links asset['alias'] wins (an instance without an entity of its own leaves
        its assets to the next one up)."""
        decl = self.declaring(ctx[-1][0], alias)
        if decl is None:
            return set(), "alias not declared in any enclosing type"
        for t, node in reversed(ctx[1:]):
            hit = self.entity_hits(node.get("Name") or "" if node is not None else t.name, alias)
            if hit:
                return hit, None
        out = self.entity_hits(ctx[0][0].name, alias, project=ctx[0][0].parent is None)
        if not out and all(t is not decl for t, _ in ctx):     # declared by a type reached through a namespace
            out = self.holders_for(decl, alias)
        return out, None if out else "no level entity links the alias on an enclosing instance"

    def bound(self, ctx, node, port, seen):
        """What input `port` of a node in the context's last type receives: its <Asset Name=port Alias=..> and its
        <Edge To=port>. Returns (holders, reasons it stopped, whether anything was bound)."""
        out, stops, any_bound = set(), [], False
        for a in node.findall("Asset"):
            if a.get("Name") == port and a.get("Alias"):
                any_bound = True
                h, why = self.alias(ctx, a.get("Alias"))
                out |= h
                if why:
                    stops.append(why)
        for e in node.findall("Edge"):
            if e.get("To") == port and e.get("From"):
                any_bound = True
                h, s = self.value(ctx, e.get("From"), seen)
                out |= h
                stops += s
        return out, stops, any_bound

    def value(self, ctx, src, seen):
        """The holders an edge source `src` (an input port of the context's type, or node.outputPort) can carry.
        Switch/Select nodes carry every holder bound to their value inputs; a module instance's output is followed
        into that module's Output node; an input port is followed to what this instance's node binds to it."""
        key = (tuple(id(n) for _, n in ctx), src)
        if key in seen or len(seen) > 300:
            return set(), []
        seen = seen | {key}
        t = ctx[-1][0]
        if "." not in src:
            if src not in t.in_ports:
                return set(), ["holder is a computed value"]
            inst = ctx[-1][1]
            if inst is None:
                return set(), ["port-fed module never instantiated"]
            h, s, b = self.bound(ctx[:-1], inst, src, seen)
            return h, s if b else ["port left unbound"]
        name, port = src.split(".", 1)
        node = t.nodes.get(name)
        if node is None:
            return set(), ["holder is a computed value"]
        inputs = {a.get("Name") for a in node.findall("Asset")} | {e.get("To") for e in node.findall("Edge")}
        if node.tag in ("Switch", "Select"):    # Value1 <- AValue1, BValue1, trueValue1, DefaultValue1, ...
            pick = [i for i in inputs if i and i != port and i.endswith(port)]
        elif node.tag == "MakeArray":
            pick = [i for i in inputs if i]
        elif "Array" in inputs:                 # ElementAt and other array readers
            pick = ["Array"]
        else:
            pick = None
        if pick is not None:
            out, stops = set(), []
            for inp in sorted(pick):
                h, s, _ = self.bound(ctx, node, inp, seen)
                out |= h
                stops += s
            return out, stops
        c = t.node_types.get(name)
        if c is not None:
            out, stops = set(), []
            inner = ctx + ((c, node),)
            for o in c.nodes_all:
                if o.tag == "Output":
                    h, s, _ = self.bound(inner, o, port, seen)
                    out |= h
                    stops += s
            return out, stops or ["holder is a computed value"]
        return set(), ["holder is a computed value"]

    def handler(self, ctx, h):
        """-> (set of (level, holder id), reason when empty)"""
        out, stops, b = self.bound(ctx, h, "CutsceneHolder", frozenset())
        if not b:
            return set(), "handler has no holder"
        return out, None if out else (stops[0] if stops else "holder unresolved")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    env = os.environ.get("KCD2MP_INSTALL")
    ap.add_argument("data", nargs="?", default=os.path.join(env, "Data") if env else DEFAULT_DATA)
    ap.add_argument("--all-projects", action="store_true", help="also read the test/debug quest projects")
    args = ap.parse_args()
    t0 = time.time()

    names = load_cutscene_names(args.data)
    levels = load_levels(args.data)
    types = load_skald(args.data, args.all_projects)
    R = Resolver(levels)

    # every handler, in every instance chain of its module -> holders
    by_holder = collections.defaultdict(set)       # (level, holder id) -> {(module, handler node, concept path)}
    hstats = collections.Counter()
    hwhy = collections.Counter()
    unnamed = collections.Counter()                # why a handler's module has no instance chain
    for t in types.values():
        hs_nodes = list(t.root.iter("CutsceneHandler"))
        if not hs_nodes:
            continue
        ctxs = R.contexts(t)
        named = bool(ctxs)
        if not named:
            u = t
            while u.parent is not None and u.instances:
                u = u.instances[0][0]
            unnamed["module never instantiated" if u is t else "a module above it is never instantiated"] += len(hs_nodes)
            ctxs = [R.lexical_context(t)]
        hstats["instances"] += len(ctxs) if named else 0
        for h in hs_nodes:
            hstats["handlers"] += 1
            if h not in t.nodes_all:
                hstats["nested"] += 1
            found, whys = False, []
            for ctx in ctxs:
                hs, why = R.handler(ctx, h)
                cp = R.concept_path(ctx, h.get("Name")) if named else ""
                for k in hs:
                    by_holder[k].add((module_path(t), h.get("Name"), cp))
                found |= bool(hs)
                if why:
                    whys.append(why)
            if found:
                hstats["resolved"] += 1
            else:
                hwhy[whys[0] if whys else "holder unresolved"] += 1

    # cutscene name -> holders
    holders_of = collections.defaultdict(list)
    for lv in levels:
        for eid, cs in lv.holders.items():
            if cs:
                holders_of[cs].append((lv, eid))
    linked = collections.defaultdict(list)          # (level, holder id) -> [(asset entity name, alias)]
    for lv in levels:
        for src, lst in lv.assets.items():
            for a, tid in lst:
                if tid in lv.holders:
                    linked[(lv.name, tid)].append((lv.ents.get(src, ("?",))[0], a))

    lines, unresolved, multi = set(), collections.defaultdict(list), {}
    for nm in sorted(set(names)):
        hs = holders_of.get(nm, [])
        if not hs:
            unresolved["no CutsceneHolder entity in any level names it"].append(nm)
            continue
        rows = set()
        for lv, eid in hs:
            for mod, node, cp in by_holder.get((lv.name, eid), ()):
                rows.add((nm, lv.ents[eid][0], mod, node, cp))
        if rows:
            lines |= rows
            if len({r[4] for r in rows}) > 1 or len({(r[2], r[3]) for r in rows}) > 1:
                multi[nm] = len({r[4] for r in rows})
        elif not any(linked.get((lv.name, eid)) for lv, eid in hs):
            unresolved["holder exists but no quest links it as an asset"].append(nm)
        else:
            unresolved["holder linked as an asset, but no CutsceneHandler resolves to it"].append(nm)

    out = sys.stdout
    out.reconfigure(encoding="utf-8", newline="\n")
    for row in sorted(lines, key=lambda r: (r[0].lower(), r)):
        out.write("|".join(row) + "\n")

    err = sys.stderr
    resolved = len({r[0] for r in lines})
    err.write("cutscene table: %d IngameCutscene rows (%d distinct names)\n" % (len(names), len(set(names))))
    err.write("resolved: %d names, %d lines\n" % (resolved, len(lines)))
    err.write("unresolved: %d names\n" % sum(len(v) for v in unresolved.values()))
    for why, lst in sorted(unresolved.items(), key=lambda kv: -len(kv[1])):
        err.write("  %3d  %s: %s\n" % (len(lst), why, " ".join(lst)))
    err.write("names with several handlers / concept paths: %d%s\n" % (len(multi), (" (" + ", ".join(
        "%s x%d" % kv for kv in sorted(multi.items())) + ")") if multi else ""))
    err.write("lines without a concept path: %d\n" % sum(1 for r in lines if not r[4]))
    err.write("handlers: %d in %d quest types, %d resolved to a holder; %d module instance chains; %d handlers not"
              " directly in their graph's Nodes\n" % (hstats["handlers"], len(types), hstats["resolved"],
                                                      hstats["instances"], hstats["nested"]))
    for why, k in hwhy.most_common():
        err.write("  %3d  %s\n" % (k, why))
    for why, k in unnamed.most_common():
        err.write("  %3d  handlers without a concept path: %s\n" % (k, why))
    if R.truncated:
        err.write("  instance chains capped at %d for: %s\n" % (R.MAX_CONTEXTS, " ".join(sorted(R.truncated))))
    err.write("levels: %s; %.1f s\n" % (", ".join("%s (%d holders)" % (lv.name, len(lv.holders)) for lv in levels),
                                        time.time() - t0))


if __name__ == "__main__":
    main()
