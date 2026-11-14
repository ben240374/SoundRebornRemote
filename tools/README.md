# Tools

Small scripts used to maintain the project. None of them is needed to build the app.

| Script | What it does | Run |
|---|---|---|
| `check.py` | Static review of the sources: XAML bindings that resolve, translation keys present in the five languages, dependency injection, a few known C# pitfalls. Not a compiler. | `python tools/check.py` |
| `build_manuals.py` | Rebuilds `docs/manual/xx.html` from `docs/manual/xx.md` (needs `pip install markdown`). | `python tools/build_manuals.py docs` |
| `build_pdfs.js` | Prints the five HTML manuals to `docs/manual/pdf` (needs Node and Playwright). | `node tools/build_pdfs.js docs` |

---

# Outils

Petits scripts d'entretien du projet. Aucun n'est nécessaire pour compiler l'application.

| Script | Rôle | Lancement |
|---|---|---|
| `check.py` | Relecture statique des sources : liaisons XAML résolues, clés de traduction présentes dans les cinq langues, injection de dépendances, quelques pièges C# connus. Ce n'est pas un compilateur. | `python tools/check.py` |
| `build_manuals.py` | Reconstruit `docs/manual/xx.html` à partir de `docs/manual/xx.md` (demande `pip install markdown`). | `python tools/build_manuals.py docs` |
| `build_pdfs.js` | Imprime les cinq manuels HTML dans `docs/manual/pdf` (demande Node et Playwright). | `node tools/build_pdfs.js docs` |
