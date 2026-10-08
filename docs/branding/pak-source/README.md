# The brand pak's source

`Libs/UI/Textures/KCDLogo.dds` is the main menu's logo texture (1024x512, DXT5) that `tools/Publish-Release.ps1` packs
into `kdcmp_brand.pak`; the launcher puts that pak into the mod's folder only for a game it starts (WO-159).

* Source of truth: this folder (it was `kdcmp_brand/` at the repo root until WO-161). The artwork it is built from is
  `../KCT_txt.png`; rebuild the texture with `python tools/Build-MenuLogo.py`.
* The pak's own entry name is `Libs/UI/Textures/KCDLogo.dds`, whatever this folder is called.
