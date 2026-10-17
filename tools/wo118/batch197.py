# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-197 (never shipped): run197.py's scenarios in a row; the game restarted (and the throwaway reloaded) whenever it is gone, and a
scenario the game died in is run again once. Needs W197_SP / W190_SP, KCD2MP_INSTALL, DOTNET_ROOT.
  python batch197.py <tag> <mode> <scenario> [<scenario> ...]
Each scenario's run190/run197 output: <SP>/results/<scenario>.<mode>.out; the line per scenario on stdout. The results of a mode go to
<SP>/results/<tag>/ afterwards (the parity table reads them there)."""
import glob, os, shutil, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live

SP = os.environ['W197_SP']
HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(SP, 'results')


def stamp(): return time.strftime('%H:%M:%S')


def game_process():
    r = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq KingdomCome.exe', '/NH'], capture_output=True, text=True)
    return 'KingdomCome.exe' in r.stdout


def ensure_game():
    if live.up(): return False
    if game_process():   # a busy REST is not a dead game: wait for it (never a second instance)
        for _ in range(30):
            time.sleep(2)
            if live.up(): return False
        print(stamp(), 'the game process runs but its REST never answered in 60 s: killed, restarted', flush=True)
        subprocess.run(['taskkill', '/F', '/IM', 'KingdomCome.exe'], capture_output=True)
        time.sleep(5)
    print(stamp(), 'the game is not up: start + load', flush=True)
    subprocess.run([sys.executable, os.path.join(HERE, 'run197.py'), 'game'], timeout=600)
    subprocess.run([sys.executable, os.path.join(HERE, 'run197.py'), 'load'], timeout=400)
    time.sleep(10)
    return True


def main():
    tag, mode, scens = sys.argv[1], sys.argv[2], sys.argv[3:]
    os.makedirs(os.path.join(RES, tag), exist_ok=True)
    for s in scens:
        for attempt in (1, 2):
            ensure_game()
            out = os.path.join(RES, '%s.%s.out' % (s, mode))
            t0 = time.time()
            with open(out, 'w', encoding='utf-8') as f:
                r = subprocess.run([sys.executable, os.path.join(HERE, 'run197.py'), s, mode], stdout=f, stderr=subprocess.STDOUT, timeout=900)
            died = not game_process()
            print(stamp(), s, mode, 'exit=%d' % r.returncode, '%.0f s' % (time.time() - t0), 'GAME DIED' if died else '', 'attempt %d' % attempt, flush=True)
            if not died and r.returncode == 0: break
        for f in glob.glob(os.path.join(RES, '%s.%s.*' % (s, mode))):
            shutil.copy(f, os.path.join(RES, tag, os.path.basename(f)))
    print('BATCH-DONE', flush=True)


if __name__ == '__main__':
    main()
