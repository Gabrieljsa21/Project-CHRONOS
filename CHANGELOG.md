# Changelog

Este arquivo registra as mudanças importantes do projeto. O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto usa [SemVer](https://semver.org/lang/pt-BR/).

## [Unreleased]

## [0.2.1] - 2026-10-25

### Alterado

- A ordem dos desbloqueios volta a seguir a raridade, da mais comum para a mais rara (`desordem` passa de 0.15 para 0 por padrão). O "sem padrão" pedido na 0.2.0 era sobre o ritmo (pedido do usuário: "eu estava me referindo ao padrao de todas no msm tempo, a cd 15min por exemplo, ter a variação, pode seguir na ordem"); o intervalo sorteado entre `intervalo_minimo_minutos` e `intervalo_maximo_minutos` e o ruído de `variacao` continuam.

## [0.2.0] - 2026-10-25

### Adicionado

- Comando `--biblioteca`: imprime em JSON (`[{"appid", "nome"}]`) todo jogo que a conta pode abrir, incluindo a biblioteca compartilhada da família, que a Web API da Steam não devolve. Usa o mesmo método do SAM.Picker (lista `games.xml` do SAM, guardada por 1 dia em `%APPDATA%\Chronos\games.xml`, e `IsSubscribedApp` jogo a jogo). Precisa da Steam aberta. A GAIA usa este comando na janela "Configurar CHRONOS".
- Conquistas de DLC que a conta não tem ficam de fora (situação `sem_dlc`). Os Achievement Sets da Steam (beta de 2026-10) gravam no schema local (`appcache/stats/UserGameStatsSchema_<appid>.bin`) a DLC de cada conquista, e a Steam recusa essas conquistas para quem não tem a DLC. A sessão confere a posse com `IsSubscribedApp` (vale a biblioteca da família) a cada abertura; se a DLC chegar, as conquistas voltam a pendentes. `--status` e `--plano` mostram quantas ficaram de fora.

- Os jogos abertos aparecem como "Jogando <jogo>" no Discord (pedido do usuário: "quero q o discord consiga reconhecer q esses jogos estão ativos"), pelo Rich Presence com o ID oficial do jogo na lista de detectáveis do Discord (mapa appid da Steam -> ID guardado por 1 dia em `%APPDATA%\Chronos\discord_jogos.json`). Não depende do executável, que o Discord nem tem cadastrado para todo jogo (007 First Light). Desliga com `"discord": false`.
- `jogos_simultaneos: 0` = sem limite de jogos abertos ao mesmo tempo.

### Alterado

- Conquistas desbloqueadas só em simulação voltam a pendentes quando a simulação é desligada (na próxima abertura da sessão) e saem de verdade no horário que já tinham.
- Cronograma novo (pedido do usuário: "qnt mais dificil e menos %, mais demorado" e "n quero q a liberação de conquistas tenham um padrão"): a espera até cada conquista, contada desde a anterior, é um espaço sorteado entre `intervalo_minimo_minutos` e o novo `intervalo_maximo_minutos` (15 e 30 por padrão; antes era de 3 a 12 min) mais uma fatia da duração proporcional à dificuldade dela, `ln(100/p)^curva`. As comuns saem a cada 15-40 min e as de 2% levam cerca de 2 h cada. A ordem segue a raridade só como tendência (desvio gaussiano em `desordem`, 0.15 por padrão; 0 volta à ordem estrita).
- Duração padrão de jogo sem tempo mediano no SteamHunters subiu de 20 h para 50 h (`duracao_padrao_horas`; pedido do usuário: "acho q 20h em media p platina de jogos sem tempo é pouco").
- Na primeira sessão de cada jogo, depois de conferir com a Steam o que já estava feito, o cronograma é refeito só com as pendentes, numa fatia da duração proporcional ao peso delas: quem já tinha as comuns não espera por elas de novo (bug real: no Edge Of Eternity, com 33 de 58 feitas, a primeira pendente só saía com 14 h de jogo). Estados antigos (sem `sincronizado`) são refeitos do mesmo jeito na próxima sessão.

## [0.1.0] - 2026-10-25

### Adicionado

- Agendador em segundo plano que mantém os jogos da lista "abertos" na Steam dentro das janelas de horário e desbloqueia as conquistas aos poucos, via `SAM.API.dll`.
- Cronograma por jogo a partir do SteamHunters (tempo mediano para 100% e raridade de cada conquista), com fallback para as porcentagens globais da Steam.
- Modo simulação, comandos `--status` e `--plano <appid>`, progresso salvo entre reinícios.
- Script `scripts/instalar_inicializacao.ps1` para subir no login do Windows.
