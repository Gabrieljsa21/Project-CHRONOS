# Arquitetura

## Alvo: .NET Framework 4.8, x86

A `SAM.API.dll` é um assembly .NET Framework 4.8 de 32 bits que conversa com a `steamclient.dll`. O Chronos segue o mesmo alvo para referenciá-la direto. A DLL não é copiada para a saída: em tempo de execução, o `AssemblyResolve` a carrega da `pasta_sam` do `config.json`.

## Dois tipos de processo

- **Agendador** (`Chronos.exe` sem argumentos): a cada 30 s relê o `config.json`, cria planos que faltam e decide quais jogos ficam abertos (janelas de horário, `jogos_simultaneos`, ordem da lista). Um mutex `Local\Project-CHRONOS` garante instância única.
- **Sessão** (`Chronos.exe --sessao <appid> --pai <pid>`): um processo por jogo, porque a SAM.API só se conecta a um appid por processo. Enquanto o processo vive, a Steam mostra o jogo como aberto e conta horas. A sessão sai sozinha quando o agendador some; o agendador mata a sessão quando ela sai da janela.

Códigos de saída da sessão: 0 ok, 1 encerrada por fora ("Finalizar tarefa" do Gerenciador de Tarefas usa o código 1), 2 sem estado, 3 falha ao conectar na Steam, 4 Steam não devolveu as estatísticas, 5 config.json inválido. O código 1 deixa o jogo parado até o agendador reiniciar (`Pausados`, só em memória); qualquer outro diferente de 0 bloqueia o jogo por 5 min antes de nova tentativa.

## Um processo por jogo

`ProcessoSessao.Preparar` copia o `Chronos.exe` com o nome do executável que a Steam abre para o jogo, e a sessão roda dessa cópia (pedido do usuário na 0.5.0: "mude o nome o processo do jogo q criamos p ser identico aoq a steam abre"). O nome vem do appinfo do cliente da Steam, `SteamApps001.GetAppData(appid, "config/launch/N/executable")`, na primeira opção de início para Windows (`oslist` vazio ou com `windows`, `type` vazio, `default` ou `none`), só o nome do arquivo (`Retail/007FirstLight.exe` vira `007FirstLight.exe`). A Steam informa isso até para jogo não instalado. O agendador consulta com `Initialize(0)` uma vez por jogo e guarda em memória; com a Steam fechada, a cópia leva o nome do jogo provisoriamente e troca na próxima sessão.

Dois jogos podem ter o mesmo executável (`Game.exe`), então cada cópia fica em `sessoes/<appid>/`, junto com as DLLs da pasta do `Chronos.exe` e o `.exe.config` (redirecionamentos de assembly, procurados pelo nome do exe). A aba "Processos" do Gerenciador de Tarefas mostra a `FileDescription` do recurso de versão, ou o nome do arquivo se não houver. Com o jogo instalado (`appmanifest_<appid>.acf` em alguma pasta do `libraryfolders.vdf`), a cópia recebe o `VS_VERSIONINFO` do executável real (`GetFileVersionInfo`, cortado no `wLength` do cabeçalho); sem ele, o recurso é apagado, e o Gerenciador mostra o nome do arquivo (`BeginUpdateResource`/`UpdateResource`, RT_VERSION 1, idioma neutro). O ícone continua o do Chronos e o processo continua de 32 bits. A cópia é refeita quando o `Chronos.exe` é recompilado; executáveis de outro nome na pasta do appid e as cópias `CHRONOS - <jogo>.exe` da 0.4.0 são apagados. Se a cópia falhar (arquivo em uso), a sessão roda do próprio `Chronos.exe`.

Até a 0.4.0 o nome levava o prefixo `CHRONOS - ` para o Discord não confundir a cópia com o jogo. Com o nome real, o Discord pode detectar a cópia como o jogo; como o Rich Presence da sessão já usa o ID oficial do jogo no Discord, os dois apontam para a mesma atividade. Quem precisa achar as sessões por fora (a GAIA, ao parar) reconhece pelo caminho, dentro da pasta do build, nunca pelo nome.

## Biblioteca (`--biblioteca`)

A Web API da Steam (`GetOwnedGames`) só devolve os jogos próprios; a biblioteca compartilhada da família exige um token de sessão do navegador, que expira. O Chronos pergunta direto ao cliente da Steam, como o SAM.Picker: baixa a lista de jogos com conquistas do SAM (`https://gib.me/sam/games.xml`, só os sem `type`, guardada por 1 dia; sem internet, usa a cópia antiga), conecta com `Initialize(0)` e testa `SteamApps008.IsSubscribedApp` em cada appid (cerca de 90 mil, uns 10 s). O nome vem de `SteamApps001.GetAppData(appid, "name")`. A saída é uma linha JSON no stdout; erro é uma linha começando com `erro:` e código diferente de 0. Quem chama (a GAIA) junta com a Web API, que cobre os jogos próprios fora da lista do SAM.

## Manter aberto

Com `manter_aberto`, o agendador também abre sessão para os jogos da lista sem conquistas a liberar (estado concluído, ou plano que falhou porque o jogo não tem conquistas), depois dos que têm. A sessão só faz `Initialize(appid)`, o que basta para a Steam mostrar o jogo aberto e somar horas, e fica em `Aguardar` sem pedir estatísticas e sem Rich Presence no Discord. A cada ciclo, se surgir um plano com pendentes, a sessão sai com código 0 e o agendador a reabre no modo normal. A falha de planejamento tem um bloqueio próprio (`PlanoBloqueadoAte`, 30 min) para não impedir a sessão de só contar horas.

## Discord

O Discord reconhece jogo pelo nome do executável (lista `applications/detectable`) ou pelo Rich Presence. Copiar o `Chronos.exe` com o nome do executável do jogo não serve para todos: o 007 First Light não tem executável cadastrado. Por isso cada sessão abre o pipe local `discord-ipc-N` (quadro = opcode int32 + tamanho int32 + JSON), faz o handshake com o ID oficial do jogo no Discord (achado pelo `third_party_skus` com `distributor: steam` na lista de detectáveis, reduzida a um mapa appid -> ID em `discord_jogos.json`, refeito 1x por dia) e envia `SET_ACTIVITY` com o `pid` da sessão, então o Discord limpa a atividade sozinho quando a sessão termina. A cada ciclo, `Manter()` reconecta se o Discord foi fechado ou aberto depois.

## Relógio próprio

O Chronos conta o tempo jogado por conta própria (`minutos_jogados` no estado), em vez de ler as horas da Steam. Cada ciclo de 60 s soma no máximo 2 min, para não contar como jogado o tempo em que o PC hibernou. A variável de ambiente `CHRONOS_ACELERAR` multiplica o relógio, apenas para testes.

## Cronograma

`Planejador.DistribuirTempos`: dificuldade = `ln(100/p)`; peso = `max(0.05, dificuldade)^curva`. A ordem é a dificuldade (da mais comum para a mais rara); com `desordem` acima de 0 (padrão 0), leva um desvio gaussiano de `desordem` vezes a amplitude das dificuldades. A espera até cada conquista é um espaço sorteado entre `intervalo_minimo_minutos` e `intervalo_maximo_minutos` mais `peso * escala * (1 ± variacao)`, com `escala = (duração - soma dos espaços) / soma dos pesos`, então o total continua sendo a duração. Objetivo: quanto mais rara, mais demora, sem ritmo reconhecível entre uma conquista e outra.

`Planejador.Replanejar`: na primeira sessão (`sincronizado` falso), depois de `Sincronizar`, redistribui só as pendentes a partir de `minutos_jogados`, numa duração igual à fatia do peso delas sobre o peso total. Sem isso, as conquistas que o usuário já tinha ocupavam o começo do cronograma e a primeira pendente demorava horas.

Ao iniciar, a sessão sincroniza com a Steam: conquistas que o usuário já tem viram `ja_tinha`; nomes que não existem no schema viram `ignorada`; conquistas de DLC que a conta não tem viram `sem_dlc` (e voltam a `pendente` se a DLC chegar); com a simulação desligada, as `desbloqueada` com `simulada: true` também voltam a `pendente`. A DLC de cada conquista vem do schema que o cliente guarda em `appcache/stats/UserGameStatsSchema_<appid>.bin` (KeyValues binário, `EsquemaSteam`): com os Achievement Sets, ele tem `groups/<id>/dlcappid` e `groupid` em cada conquista. Não há Web API documentada para isso. A posse vem de `SteamApps008.IsSubscribedApp(dlc)`, que inclui a biblioteca da família. A Steam pode recusar a gravação por algumas horas (aconteceu com o The Darkness II e o Portal 2 em 2026-10-26) ou para sempre (conquista protegida pelo servidor do jogo). Por isso, a cada recusa, a sessão pede as estatísticas de novo e adia a conquista em 30, 60 e 120 min de jogo. Depois da 4ª recusa, ela vira `recusada`, que conta como resolvida no `Concluido`. A sessão seguinte abre mesmo com o jogo concluído (`TemRecusadas`), confere as recusadas com a Steam e devolve a pendentes as que a conta não tem, espaçadas de 15 a 45 min. Uma `ignorada` com `falhas > 0` é o formato da 0.5.0 e das anteriores e é tratada como `recusada`.

## Persistência

Estado por jogo em `estado/<appid>.json`, gravado por arquivo temporário + `File.Replace` (troca atômica). Log único, compartilhado entre processos, com `FileShare.ReadWrite` e rotação em 5 MB.
