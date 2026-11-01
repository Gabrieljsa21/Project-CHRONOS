# Changelog

Este arquivo registra as mudanças importantes do projeto. O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto usa [SemVer](https://semver.org/lang/pt-BR/).

## [Unreleased]

## [0.14.0] - 2026-11-01

### Alterado

- A espera até cada conquista passa a seguir a diferença de raridade para a anterior, e não mais a raridade da conquista sozinha (pedido do usuário: "se peguei uma com 90% dos jogadores tem e a proxima 85% tem, o tempo tem de ser menor doq se a proxima fosse 65%"). O peso de cada conquista é o salto `ln(100/p)^curva` desde a anterior, com o jogo inteiro em ordem. A conta parte da mais comum do jogo, porque a porcentagem dela mede quem nunca abriu o jogo. Antes, duas raras vizinhas (10% e 11%) esperavam horas entre si. No Budgie's Bug Shop, de 82,6% para 82,3% leva cerca de 4 min, e de 76,8% para 54,1%, cerca de 2 h.
- O espaço de 15 a 30 min entre conquistas, pensado para jogos longos, passa a ter um teto: um quarto do tempo médio por conquista (`intervalo_fracao`, novo, 0,25), sem descer do piso de 0,5 min. Num jogo de 19 conquistas em 3,1 h, o espaço fica entre 1,6 e 3,3 min. Os jogos longos continuam nos 15 a 30 min. Na 0.12.0, o espaço só encolhia quando a soma dos espaços passava da duração.
- Todos os planos são refeitos uma vez, na próxima sessão de cada jogo (`versao_plano` no estado, que substitui o `espacos_ajustados` da 0.12.0). O tempo total de cada jogo quase não muda. A exceção é o GooCubelets, que passa de cerca de 7,2 h para 4,5 h a partir de agora: as 5 conquistas que já estavam na conta (de 6,7% a 4,5%) cobrem 42% do salto de raridade do jogo, e quase todas as 362 pendentes estão entre 2,8% e 2,7%.

## [0.13.1] - 2026-11-01

### Alterado

- A rajada de um plano espremido solta no máximo 3 conquistas por vez, e não mais 5 (pedido do usuário: "Coloca no max 3 nas rajadas"). Na simulação do GooCubelets, as rajadas já não passavam de 3, e o plano continua terminando em cerca de 7,2 h a partir de agora.

## [0.13.0] - 2026-11-01

### Alterado

- Jogo com horas jogadas, mas sem as conquistas correspondentes, termina perto da duração dele a partir de agora (pedido do usuário: "Se eles pedem 7h, tem de terminar proximo de 7h de agr em diante. Mmsm ja tendo passado 8h"). Quando nem os intervalos cabem no que resta da duração, as horas já jogadas deixam de contar, e as pendentes ganham a fatia de peso delas na duração. Na 0.12.0, o GooCubelets (8,1 h jogadas de 7,3 h, 362 de 367 pendentes) ainda ia até cerca de 20 h. Agora termina em cerca de 7,2 h a partir de agora. Mudam também o Balloon hater (18,3 h), o Bones and Coins (6,6 h), o Budgie's Bug Shop (2,5 h) e o Chef Knight (1,8 h). Isso vale para jogo novo que chega com muitas horas e nenhuma conquista. Os demais jogos não mudam.
- O piso do intervalo (`intervalo_piso_minutos`) cai de 1 para 0,5 min.

### Adicionado

- Rajadas: num plano espremido (próxima conquista marcada a menos de 2 min), a sessão que pega a vez solta junto as que já venceram, até 5 e com 3 a 10 s entre elas, como quem termina uma fase. Antes saía uma por vez a cada 1 a 2 min, o que esticava o GooCubelets de 7,2 h para cerca de 12 h. Nesses planos, o atraso de um desbloqueio não empurra mais as seguintes. Planos com intervalos de 15 a 30 min continuam soltando uma por vez.

## [0.12.0] - 2026-11-01

### Alterado

- O intervalo entre conquistas encolhe quando não cabe na duração do jogo (pedido do usuário: "tem de corrigir, fazer algo entre a opcao 1 e 2"). O GooCubelets tem 367 conquistas e 7,3 h de duração (a maior entre o SteamHunters, com 5,4 h, e o HowLongToBeat, com 7,3 h), mas a Steam já registrava 8,1 h. Com isso, as 362 pendentes recebiam só os intervalos de 15 a 30 min, e o plano terminava em 143,9 h. Agora, quando o número de conquistas vezes a média do intervalo passa da duração disponível, o mínimo e o máximo encolhem na mesma proporção, sem que o mínimo fique abaixo de `intervalo_piso_minutos` (novo, 1 min por padrão) e com o máximo sempre no dobro do mínimo. O GooCubelets passa a terminar em cerca de 17 h (1 a 2 min entre conquistas) e o Balloon hater, em 19,4 h em vez de 34,7 h (3 a 6 min). Jogos cujos intervalos já cabem na duração não mudam. A opção vale para todos os jogos e fica no `config.json`.
- Os planos já feitos que terminam depois da duração total são refeitos uma vez, na próxima sessão (`espacos_ajustados` no estado).

## [0.11.1] - 2026-11-01

### Alterado

- A duração total passa a ser sempre a maior das fontes, também quando as três têm o jogo (pedido do usuário: "pode trocar p sempre a maior"). A 0.11.0 usava a mediana das três. No Rise of the Tomb Raider, isso deixava valer o SteamHunters (83,7 h) entre o HowLongToBeat (35,4 h) e o curador (107,5 h); agora valem 107,5 h, e no Portal 2, 35 h em vez de 33,5 h. Os planos que a 0.11.0 já tinha calculado pela mediana são refeitos na próxima sessão.

## [0.11.0] - 2026-11-01

### Alterado

- A duração total do jogo passa a ser conferida em três fontes (pedido do usuário: "quero q seja automatico e consiga validar o tempo em umas 3 fontes pelo menos"). Além da mediana do SteamHunters, entram o tempo de 100% do HowLongToBeat e o do curador Average Achievement Hunter da Steam. Com as três, vale a mediana delas; com uma ou duas, a maior. A mediana do SteamHunters cai com quem libera as conquistas pelo SAM: o Bitburner tinha 51 min de duração no SteamHunters, contra 1.645 h no HowLongToBeat (e um amigo do usuário passou de 1.500 h). A duração de cada fonte vai para o log, para o `--plano` e para o estado (`fonte_duracao`, `tempos_fontes`). `duracao_horas` no `config.json` continua vencendo as fontes.
- Os planos atuais têm a duração refeita uma vez, na próxima sessão de cada jogo. Nos jogos em andamento, mudam mais: Bitburner (0,8 h para 1.646 h), Melvor Idle (513 h para 1.670 h), Desktop Fishing (868 h para 1.507 h), GWENT (169 h para 353 h) e Idle Wizard (2.113 h para 2.401 h). Se o HowLongToBeat ou o curador estiver fora do ar, a duração atual fica e a sessão seguinte tenta de novo.
- Tirar a `duracao_horas` de um jogo no `config.json` agora volta a duração para a das fontes. Antes, o valor do config continuava guardado no plano.

## [0.10.0] - 2026-10-29

### Adicionado

- A sessão reconfere com a Steam as conquistas que o plano já dá como obtidas (`ja_tinha` e `desbloqueada`). A que foi removida da conta volta a pendente (pedido do usuário: "Removi as conquistas p adquiri-las novamente, o chronos vai pegar elas?"). Em 2026-10-29, o usuário trocou o save do Black Myth: Wukong por um 100% e abriu o jogo real, que soltou 8 conquistas em 19 s. Depois ele as removeu, mas o CHRONOS continuava com o jogo concluído. Antes, a sincronização só conferia pendentes, `sem_dlc` e recusadas.
- Conquista que o jogo ganha depois do plano entra como pendente (pedido do usuário: "tem jogo q lança novas conquistas com o tempo tbm"). A lista vem do schema local da Steam (`EsquemaSteam.Conquistas`). O nome e a porcentagem vêm do SteamHunters ou, se ele ainda não tem a conquista, da Steam. As que o SteamHunters marca como impossíveis vão para `descartadas` no estado e não voltam a ser conferidas. Se o SteamHunters falhar num plano feito com ele, nada entra e a conferência seguinte tenta de novo.
- A conferência roda no início de toda sessão e, num jogo concluído aberto só para contar horas (`manter_aberto`), a cada 30 min. Quando alguma conquista volta a pendente, o cronograma é refeito e a sessão reabre no modo normal.

### Alterado

- As conquistas que voltam a pendentes (removidas, novas ou de DLC que chegou) ganham pelo menos a fatia de peso delas na duração total do jogo, mesmo quando as horas jogadas já passaram dessa duração (pedido do usuário: "quero q tenha a diferença de horas"). Sem isso, elas teriam só os intervalos de 15 a 30 min. As 8 do Wukong ficam com cerca de 23 h. O replanejamento comum continua terminando na duração total, como na 0.8.1.

## [0.9.0] - 2026-10-28

### Adicionado

- O agendador regrava `saude.json` a cada ciclo de 30 s, com a hora e a lista do que parou de funcionar (pedido do usuário: "tem q ter um ! ou algo na tela p indicar q tem algo errado, p mim saber qnd parou de funcionar algo"). Entram na lista: a Steam fechada; o jogo encerrado pelo Gerenciador de Tarefas, que fica parado até o CHRONOS reiniciar; a sessão que caiu 2 ou mais vezes seguidas, com o motivo da última queda; o jogo cujo plano de conquistas não montou; e o erro no ciclo do agendador. Uma queda isolada, como a da Steam reiniciada, não entra. A contagem zera quando a sessão fica 10 min de pé ou termina normalmente. A GAIA lê o arquivo e acende o "!" do indicador. Se o arquivo parar de ser atualizado, a GAIA conclui que o agendador travou.

### Alterado

- Entre dois desbloqueios, de qualquer jogo, passam de 1 a 2 min (pedido do usuário: "n quero q varias conquistas sejam habilitadas no msm minuto, pode por uma variação de 1~2min"). Cada sessão já soltava só uma por ciclo, mas as sessões não se coordenavam: às 16:30 de 2026-10-28, 14 sessões reabriram juntas com conquista vencida e soltaram 14 no mesmo minuto. Agora a sessão pede a vez antes de desbloquear (`Vez.Pegar`, mutex `Local\Project-CHRONOS-desbloqueio` e `proximo_desbloqueio.txt` com a hora em que a próxima fica liberada). Sem a vez, ela tenta no ciclo seguinte. Quando a conquista sai atrasada, as seguintes do mesmo jogo andam o mesmo tanto, como já acontecia depois de uma recusa.

## [0.8.2] - 2026-10-28

### Corrigido

- Depois que a Steam reiniciava, os jogos iam parando um a um e só voltavam quando o CHRONOS reiniciava (relato do usuário: "o chronos ta iniciado mas n parece ta ativo"). Em 2026-10-28 a Steam reiniciou às 10:20, e 14 das 17 sessões caíram nas horas seguintes. Uma sessão presa à Steam antiga cai com o código 1, o mesmo do "Finalizar tarefa", e o agendador pausava o jogo como se o usuário o tivesse encerrado. Agora o agendador guarda a Steam (`steam.exe`) aberta quando cada sessão começa. Se a sessão sai com o código 1 e a Steam mudou ou fechou, o jogo é reaberto em 5 min em vez de pausar. A sessão também confere a Steam a cada minuto: se ela reiniciou, salva o estado e sai com o código 6 para ser reaberta, sem esperar a próxima conquista.
- A sessão caía com `IOException` ao gravar o estado quando o arquivo estava sendo lido no mesmo instante (o Cookie Clicker caiu às 13:16 e às 14:42). `EstadoJogo.Salvar` tenta a troca do arquivo até 10 vezes, com 200 ms de intervalo, como o `Carregar` já fazia.

## [0.8.1] - 2026-10-28

### Corrigido

- O plano passava da duração total do jogo (relato do usuário: "O do how to train msm, mediana dele é 113, e vou jogar 159, ta muito acima"). A 0.8.0 dava às pendentes o maior entre o que faltava da duração total e a fatia de peso delas. Perto da mediana, a fatia de peso das raras vencia. Agora o plano termina na duração total: as pendentes ocupam o que falta dela depois das horas da Steam. Quem já passou da duração total recebe só os intervalos de 15 a 30 min entre as conquistas. A fatia de peso continua só para jogo sem horas registradas. Os planos refeitos pela 0.8.0 foram refeitos de novo (`minutos_steam` apagado do estado).

## [0.8.0] - 2026-10-28

### Adicionado

- O cronograma conta as horas que a Steam já registrou para o jogo (pergunta do usuário: "o chronos n considera qnts horas ja joguei e a qnt de conquistas q ja desbloqueei? E o cookie clicer deveria pedir umas 2k horas p platinar, pq ta so 50?"). As conquistas que a conta já tinha já saíam do cronograma; as horas, não. A sessão lê o `Playtime` do `localconfig.vdf` da conta logada (`TempoSteam`), grava em `minutos_steam` e começa o relógio dali. As pendentes ocupam o que falta da duração total depois dessas horas, nunca menos que a fatia de peso delas, que era a única regra até a 0.7.0. No Cookie Clicker (596 de 637 conquistas, 1.457 h na Steam), as 39 pendentes ficaram com 50 h porque a mediana do SteamHunters é 260 h, puxada para baixo por quem usa o SAM; com `duracao_horas: 2000`, passam a 543 h.
- Mudar `duracao_horas` de um jogo no `config.json` refaz o cronograma na próxima sessão; antes só valia para planos novos. O estado guarda a duração do jogo inteiro em `duracao_total_minutos`. Planos da 0.7.0 e anteriores são refeitos uma vez, com a duração total buscada de novo no SteamHunters.

## [0.7.0] - 2026-10-27

### Adicionado

- O agendador reordena a lista de jogos do `config.json`: um jogo concluído (sem conquista pendente) sobe para o topo, o último concluído primeiro (pedido do usuário: "o chronos tem de reordenar os jogos na fila, p qnd um ficar completo, ir para o topo"). Os outros mantêm a ordem que o usuário escolheu, e os jogos com conquistas a liberar continuam com prioridade nas vagas de `jogos_simultaneos`, então a ordem de desbloqueio não muda. Só a lista `jogos` é regravada (`Config.SalvarOrdemJogos`, troca atômica); os outros campos do arquivo ficam como estão.

## [0.6.0] - 2026-10-27

### Alterado

- As conquistas saem sempre na ordem do cronograma (pedido do usuário: "as conquistas tem de ser feita em ordem, se ela n foi concluida, espera 15min e tenta dnovo, ate ir. Se apos 1h n ir, pare e me notifique"). A conquista que a Steam recusa segura a fila e é tentada de novo a cada 15 min, com um novo pedido de estatísticas. Antes, a 0.5.1 a mandava para o fim da fila. Depois de 1 h de recusas na mesma sessão, o jogo para: o estado ganha `parado_em` e `parado_na` (api_name da conquista), nenhuma conquista sai e, com `manter_aberto`, o jogo continua aberto só para contar horas. Quando a conquista presa sai, as seguintes andam o mesmo tanto de minutos, para não saírem em bloco. A situação `recusada` deixa de ser criada; as da 0.5.1 voltam a pendentes na sincronização.

### Adicionado

- `Chronos.exe --retomar <appid>` tira o jogo do estado parado; a GAIA chama esse comando pelo botão "Tentar de novo". `--status` mostra os jogos parados.

## [0.5.1] - 2026-10-27

### Corrigido

- The Darkness II aparecia como concluído com 43 de 50 conquistas na Steam, e o jogo nem tem DLC (relato do usuário: "o jogo darkness 2 esta como completo, mas na steam ta 43/50"). Entre 16h45 e 19h03 de 2026-10-26, a Steam recusou toda gravação daquele jogo e voltou a aceitar depois; o Portal 2 teve o mesmo problema por meia hora. O CHRONOS fazia 3 tentativas em 3 min, marcava a conquista como `ignorada` e o jogo contava como concluído. Agora, depois de cada recusa, a sessão pede as estatísticas de novo e manda a conquista para o fim da fila por 30, 60 e 120 min de jogo. Depois da 4ª recusa, ela vira `recusada` (situação nova). Cada sessão que abre confere as recusadas com a Steam: as que a conta já tem viram `ja_tinha`, e as outras voltam a pendentes, espaçadas de 15 a 45 min a partir do minuto atual. A sessão abre mesmo com o jogo concluído. As `ignorada` com falhas das versões anteriores contam como recusadas, então as 7 do The Darkness II voltam sozinhas. `--status` mostra quantas há.

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
