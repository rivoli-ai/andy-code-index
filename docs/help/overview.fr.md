---
title: Présentation d'Andy Code Index
slug: andy-code-index-overview
order: 1
tags: [code, search, embeddings]
---

# Présentation d'Andy Code Index

Andy Code Index est le service de recherche sémantique de code de l'écosystème Andy. Il découvre les dépôts, parcourt les arborescences de fichiers, calcule les plongements et sert une recherche hybride (sémantique + mots-clés) avec citations au niveau du fichier.

## Ce qu'il fait

- Indexe chaque fichier de chaque dépôt enregistré — agnostique au langage, parsé par tree-sitter quand disponible.
- Stocke les plongements dans PostgreSQL via pgvector ; exécute la recherche hybride en fusionnant la similarité cosinus pgvector avec les hits par mots-clés `tsvector` PostgreSQL.
- Retourne des citations (`chemin:plage-de-lignes`) pour que l'UI appelante puisse créer un lien profond vers l'éditeur.
- Réindexe de façon incrémentale lors d'un `git pull` ; la réindexation complète est rare.
- Alimente le RAG du panneau de chat lorsque la conversation est ancrée dans le contenu du dépôt.

## Concepts clés

- **Citation** — un tuple `(repo, chemin, ligneDébut, ligneFin)`. Chaque hit de recherche en porte une ; l'UI l'utilise pour les aperçus et la navigation par clic.
- **Score hybride** — Reciprocal Rank Fusion sur le rang du plongement et le rang des mots-clés, le même schéma qu'utilise la recherche d'aide de Conductor.
- **Fichier indexable** — fichiers texte sous un plafond de taille configurable ; les binaires et fichiers générés sont ignorés.

## Où il s'intègre

L'onglet Code de Conductor est un client mince au-dessus de Code Index. Le panneau de chat tire des citations d'ici quand un prompt référence l'état du dépôt. Dépend du PostgreSQL embarqué (avec pgvector) et d'Auth pour la validation des jetons.

## Configuration

La liste des dépôts et les seuils d'indexation résident sous `andy.code-index.*` dans `andy-settings`. La chaîne de connexion pgvector est intégrée dans le bundle PostgreSQL embarqué que Conductor livre.

## Dépannage

- **La recherche ne retourne rien pour un fichier connu** — l'index est obsolète. Déclenchez une réindexation depuis l'onglet Code ou attendez le prochain cycle de sondage.
- **« pgvector extension not found »** — le PostgreSQL embarqué n'a pas chargé l'extension. Vérifiez `~/Library/Logs/Conductor/services/postgres.log` pour les erreurs `CREATE EXTENSION vector`.
- **L'indexation est lente** — les gros dépôts mangent du temps de plongement. Ajoutez un `.codeindexignore` (style gitignore) pour ignorer les répertoires vendorisés.
