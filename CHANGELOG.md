# Changelog

Este arquivo registra as mudanças importantes do projeto. O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto usa [SemVer](https://semver.org/lang/pt-BR/).

## [Unreleased]

## [0.5.0] - 2026-10-26

### Alterado

- Cada sessão roda com o mesmo nome do executável que a Steam abre para o jogo (`My Sexy Neighbour 2.exe`, `EdgeOfEternity.exe`), no lugar de `CHRONOS - <jogo>.exe` (pedido do usuário: "mude o nome o processo do jogo q criamos p ser identico aoq a steam abre"). O nome vem do appinfo da Steam e vale também para jogo não instalado. Com o jogo instalado, a cópia leva também a descrição e a versão do executável real; sem ele, fica sem descrição, e o Gerenciador de Tarefas mostra o nome do arquivo. As cópias ficam em `sessoes/<appid>/`, com as DLLs, porque dois jogos podem ter o mesmo executável. As cópias antigas `CHRONOS - *.exe` são apagadas.

## [0.4.0] - 2026-10-26

### Adicionado

- Cada jogo roda num processo próprio, `CHRONOS - <jogo>.exe`, com o nome do jogo também na descrição, que é o que a aba "Processos" do Gerenciador de Tarefas mostra (pedido do usuário: "quero q crie um processo [...] p mim poder encerar ele e fechar pelo gerenciador de tarefas, caso queira parar apenas 1 por um momento, sem fechar tudo"). "Finalizar tarefa" num deles para só aquele jogo, até o CHRONOS reiniciar; antes, o agendador o reabria em 5 min. O nome nunca é o do executável real do jogo, para o Discord não confundir a cópia com o jogo.

### Alterado

- `config.json` inválido sai com código 5 (antes 1, que agora significa sessão encerrada pelo Gerenciador de Tarefas).

## [0.3.0] - 2026-10-25

### Adicionado

- Opção `manter_aberto` (desligada por padrão): jogo da lista sem conquistas a liberar (concluído, ou sem conquistas na Steam, como o Arknights: Endfield) continua aberto na Steam só para contar horas, como o ArchiSteamFarm (pedido do usuário: "eu quero q o jogo fique ativo para aumentar horas jogadas, mesmo quando nao tem mais achiv"). Os jogos com conquistas a liberar têm prioridade nas vagas de `jogos_simultaneos`. Esses jogos não anunciam "Jogando" no Discord. Se o jogo ganhar conquistas, a sessão reabre no modo normal. `--status` mostra quais ficam abertos só para contar horas.

### Corrigido

- Falha ao planejar um jogo deixava de bloquear a sessão dele: o bloqueio de 30 min agora vale só para a próxima tentativa de planejar.

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
