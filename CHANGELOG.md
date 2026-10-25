# Changelog

Este arquivo registra as mudanças importantes do projeto. O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto usa [SemVer](https://semver.org/lang/pt-BR/).

## [Unreleased]

## [0.1.0] - 2026-10-25

### Adicionado

- Agendador em segundo plano que mantém os jogos da lista "abertos" na Steam dentro das janelas de horário e desbloqueia as conquistas aos poucos, via `SAM.API.dll`.
- Cronograma por jogo a partir do SteamHunters (tempo mediano para 100% e raridade de cada conquista), com fallback para as porcentagens globais da Steam.
- Modo simulação, comandos `--status` e `--plano <appid>`, progresso salvo entre reinícios.
- Script `scripts/instalar_inicializacao.ps1` para subir no login do Windows.
