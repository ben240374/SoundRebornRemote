#!/usr/bin/env python3
"""Relecture mécanique du projet, faute de pouvoir compiler ici.

Ne remplace pas un compilateur : attrape les fautes que la relecture humaine
laisse passer — une liaison XAML vers un membre disparu, une clé de traduction
absente, une dépendance que l'injection ne sait pas construire, et quelques
pièges C# déjà rencontrés sur ce projet.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
APP = ROOT / "src/SoundRebornRemote.App"
CORE = ROOT / "src/SoundReborn.Core"

cs_app = list(APP.rglob("*.cs"))
cs_core = list(CORE.rglob("*.cs"))
cs_all = cs_app + cs_core
xamls = list(APP.rglob("*.xaml"))

problems = []
text_of = {f: f.read_text(encoding="utf-8") for f in cs_all}
xaml_of = {x: x.read_text(encoding="utf-8") for x in xamls}

# ------------------------------------------------------------------ membres publics
# Propriétés déclarées, plus celles qu'engendre [ObservableProperty] à partir du
# champ « _nom », plus les commandes qu'engendre [RelayCommand] sur « FaireAsync ».
members = {}


def note(name, owner):
    members.setdefault(name, set()).add(owner)


for f, text in text_of.items():
    cls = None
    lines = text.splitlines()

    for i, line in enumerate(lines):
        m = re.search(r"\b(?:sealed\s+|partial\s+|abstract\s+|static\s+)*class\s+(\w+)", line)
        if m:
            cls = m.group(1)

        # L'accolade d'une propriété peut tomber sur la ligne suivante : on regarde
        # donc aussi le début de la ligne d'après, sinon « public string AppVersion »
        # suivi de son bloc passe pour une liaison disparue.
        nxt = lines[i + 1].lstrip() if i + 1 < len(lines) else ""
        m = re.search(r"^\s*public\s+(?:static\s+|readonly\s+|required\s+|override\s+|virtual\s+)*"
                      r"[\w<>\[\],\?\.\(\) ]+?\s+(\w+)\s*(?:=>|\{|;)", line)

        if m is None and nxt.startswith("{"):
            m = re.search(r"^\s*public\s+(?:static\s+|readonly\s+|required\s+|override\s+|virtual\s+)*"
                          r"[\w<>\[\],\?\.]+\s+(\w+)\s*$", line)

        if m and cls:
            note(m.group(1), cls)

        if "[ObservableProperty]" in line:
            for j in range(i + 1, min(i + 8, len(lines))):
                fm = re.search(r"^\s*(?:private|protected)\s+[\w<>\[\],\?\.]+\s+_(\w+)\s*(?:=|;)", lines[j])
                if fm and cls:
                    note(fm.group(1)[0].upper() + fm.group(1)[1:], cls)
                    break

        if "[RelayCommand]" in line or "[RelayCommand(" in line:
            for j in range(i + 1, min(i + 8, len(lines))):
                cm = re.search(r"\s(\w+?)(Async)?\s*\(", lines[j])
                if cm and cls and not lines[j].lstrip().startswith("//"):
                    note(cm.group(1) + "Command", cls)
                    break

# ------------------------------------------------------- 1. liaisons XAML résolues
BUILTIN = {"."}

for x, text in xaml_of.items():
    for m in re.finditer(r"\{Binding\s+([A-Za-z_][\w\.]*)", text):
        path = m.group(1).split(".")[0]
        if path in BUILTIN or path in members:
            continue
        problems.append(f"Liaison introuvable : {x.name} -> {path}")

    for m in re.finditer(r"Path=([A-Za-z_]\w*)", text):
        if m.group(1) not in members:
            problems.append(f"Liaison Path introuvable : {x.name} -> {m.group(1)}")

# ------------------------------------------------- 2. clés de traduction existantes
loc = (APP / "Services/Localization.cs").read_text(encoding="utf-8")
declared = set(re.findall(r'Add\("([^"]+)"', loc))

used = set()
for text in text_of.values():
    used |= set(re.findall(r'Localization\.Get\("([^"]+)"', text))
    # Une clé choisie par un ternaire — Get(cond ? "S_A" : "S_B") — échappe au motif
    # ci-dessus. Toute chaîne qui a la forme d'une clé compte donc comme utilisée.
    used |= set(re.findall(r'"((?:L|S|D)_[A-Z][A-Za-z0-9]+)"', text))
for text in xaml_of.values():
    used |= set(re.findall(r"DynamicResource\s+(L_\w+)", text))

for key in sorted(used - declared):
    problems.append(f"Clé de traduction absente : {key}")

# chaque Add doit porter exactement six libellés (fr, en, de, nl, es, it)
for m in re.finditer(r'Add\("([^"]+)"\s*,(.*?)\);', loc, re.S):
    # Les guillemets échappés à l'intérieur d'un libellé — « \"System\" » — ne
    # délimitent rien : les retirer avant de compter, sinon une traduction
    # parfaitement complète paraît en compter sept.
    body = m.group(2).replace('\\"', "")
    if body.count('"') // 2 != 6:
        problems.append(f"Traduction incomplète : {m.group(1)} ({body.count(chr(34)) // 2} libellés)")

# ------------------------------------------------------------ 3. injection résolue
program = (APP / "MauiProgram.cs").read_text(encoding="utf-8")
registered = set(re.findall(r"AddSingleton<(\w+)>", program))
# Une interface inscrite avec son implémentation : « AddSingleton<IX, X>() ».
registered |= set(re.findall(r"AddSingleton<(\w+)\s*,", program))
registered |= {"HttpClient"}
# Un type inscrit par une fabrique (« AddSingleton<X>(services => new X(...)) »)
# reçoit ses paramètres de la fabrique elle-même : rien à vérifier pour lui.
factory_built = set(re.findall(r"AddSingleton<(\w+)>\(\s*\w+\s*=>", program))

for f, text in text_of.items():
    for m in re.finditer(r"public\s+(\w+)\s*\(([^)]*)\)\s*(?::\s*base\([^)]*\)\s*)?\{", text):
        cls, args = m.group(1), m.group(2)
        if cls not in registered or cls in factory_built:
            continue
        for arg in args.split(","):
            parts = arg.strip().split()
            # « IX? x = null » : le type sans le point d'interrogation.
            if parts:
                parts[0] = parts[0].rstrip("?")
            if len(parts) >= 2 and parts[0] not in registered and parts[0][0].isupper():
                problems.append(f"Dépendance non enregistrée : {cls} demande {parts[0]}")

# -------------------------------------------------------------- 4. pièges C# connus
base_vm = (APP / "ViewModels/BaseViewModel.cs").read_text(encoding="utf-8")
base_members = set(re.findall(r"^\s*(?:public|protected)\s+[\w<>\[\]\?\.]+\s+(\w+)\s*(?:=>|\{)",
                              base_vm, re.M))

for f, text in text_of.items():
    if f.name == "BaseViewModel.cs" or "BaseViewModel" not in text:
        continue
    for i, line in enumerate(text.splitlines(), 1):
        m = re.search(r"^\s*private\s+[\w<>\[\]\?\.]+\s+_(\w+)\s*(?:=|;)", line)
        if m:
            name = m.group(1)[0].upper() + m.group(1)[1:]
            if name in base_members:
                problems.append(f"« {name} » masque BaseViewModel : {f.name}:{i} (CS0108)")

for f, text in text_of.items():
    for i, line in enumerate(text.splitlines(), 1):
        if re.search(r"\bparams\s+[\w<>\[\]\?,\.]+(?:\[\])\s+\w+\s*,", line):
            problems.append(f"« params » n'est pas le dernier paramètre : {f.name}:{i} (CS0231)")

for x, text in xaml_of.items():
    if "ScrollView.GestureRecognizers" in text:
        problems.append(f"ScrollView.GestureRecognizers bloque le défilement : {x.name}")

# ------------------------------------------- 5. API disparues encore référencées
gone = ["GetZonePermanentAsync"]
for f, text in text_of.items():
    for name in gone:
        if f.parent.name != "SoundReborn.Core" and name in text:
            problems.append(f"API supprimée encore appelée : {f.name} -> {name}")

# ---------------------------------------------- 6. gabarits de traduction cohérents
# Le nombre de {n} doit être le même dans les six langues d'une clé.
for m in re.finditer(r'Add\("([^"]+)"\s*,(.*?)\);', loc, re.S):
    labels = re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(2))
    holes = [tuple(sorted(set(re.findall(r"\{(\d)\}", label)))) for label in labels]
    if len(set(holes)) > 1:
        problems.append(f"Gabarit incohérent entre langues : {m.group(1)} {holes}")

# ------------------------------------------------ 7. arguments fournis aux gabarits
table = {}
for m in re.finditer(r'Add\("([^"]+)"\s*,\s*"((?:[^"\\]|\\.)*)"', loc):
    table[m.group(1)] = len(set(re.findall(r"\{(\d)\}", m.group(2))))

for f, text in text_of.items():
    for m in re.finditer(r'Localization\.Get\("([^"]+)"\s*([,)])', text):
        key, nxt = m.group(1), m.group(2)
        if key in table and table[key] > 0 and nxt == ")":
            problems.append(f"Gabarit sans argument : {f.name} -> {key}")

# --------------------------------------------------------------------- verdict
print(f"{len(cs_all)} fichiers C#, {len(xamls)} XAML, {len(members)} membres indexés.")
print(f"Traductions : {len(declared)} clés, {len(used)} utilisées.")

if problems:
    print(f"\n{len(problems)} problème(s) :")
    for p in sorted(set(problems)):
        print("  -", p)
    sys.exit(1)

print("\nAucun problème détecté.")
