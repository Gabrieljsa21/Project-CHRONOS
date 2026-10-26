# Arquitetura

## Alvo: .NET Framework 4.8, x86

A `SAM.API.dll` é um assembly .NET Framework 4.8 de 32 bits que conversa com a `steamclient.dll`. O Chronos segue o mesmo alvo para referenciá-la direto. A DLL não é copiada para a saída: em tempo de execução, o `AssemblyResolve` a carrega da `pasta_sam` do `config.json`.

## Dois tipos de processo

- **Agendador** (`Chronos.exe` sem argumentos): a cada 30 s relê o `config.json`, cria planos que faltam e decide quais jogos ficam abertos (janelas de horário, `jogos_simultaneos`, ordem da lista). Um mutex `Local\Project-CHRONOS` garante instância única.
- **Sessão** (`Chronos.exe --sessao <appid> --pai <pid>`): um processo por jogo, porque a SAM.API só se conecta a um appid por processo. Enquanto o processo vive, a Steam mostra o jogo como aberto e conta horas. A sessão sai sozinha quando o agendador some; o agendador mata a sessão quando ela sai da janela.

Códigos de saída da sessão: 0 ok, 2 sem estado, 3 falha ao conectar na Steam, 4 Steam não devolveu as estatísticas. Qualquer código diferente de 0 bloqueia o jogo por 5 min antes de nova tentativa.

## Biblioteca (`--biblioteca`)

A Web API da Steam (`GetOwnedGames`) só devolve os jogos próprios; a biblioteca compartilhada da família exige um token de sessão do navegador, que expira. O Chronos pergunta direto ao cliente da Steam, como o SAM.Picker: baixa a lista de jogos com conquistas do SAM (`https://gib.me/sam/games.xml`, só os sem `type`, guardada por 1 dia; sem internet, usa a cópia antiga), conecta com `Initialize(0)` e testa `SteamApps008.IsSubscribedApp` em cada appid (cerca de 90 mil, uns 10 s). O nome vem de `SteamApps001.GetAppData(appid, "name")`. A saída é uma linha JSON no stdout; erro é uma linha começando com `erro:` e código diferente de 0. Quem chama (a GAIA) junta com a Web API, que cobre os jogos próprios fora da lista do SAM.

## Discord

O Discord reconhece jogo pelo nome do executável (lista `applications/detectable`) ou pelo Rich Presence. Copiar o `Chronos.exe` com o nome do executável do jogo não serve para todos: o 007 First Light não tem executável cadastrado. Por isso cada sessão abre o pipe local `discord-ipc-N` (quadro = opcode int32 + tamanho int32 + JSON), faz o handshake com o ID oficial do jogo no Discord (achado pelo `third_party_skus` com `distributor: steam` na lista de detectáveis, reduzida a um mapa appid -> ID em `discord_jogos.json`, refeito 1x por dia) e envia `SET_ACTIVITY` com o `pid` da sessão, então o Discord limpa a atividade sozinho quando a sessão termina. A cada ciclo, `Manter()` reconecta se o Discord foi fechado ou aberto depois.

## Relógio próprio

O Chronos conta o tempo jogado por conta própria (`minutos_jogados` no estado), em vez de ler as horas da Steam. Cada ciclo de 60 s soma no máximo 2 min, para não contar como jogado o tempo em que o PC hibernou. A variável de ambiente `CHRONOS_ACELERAR` multiplica o relógio, apenas para testes.

## Cronograma

`Planejador.DistribuirTempos`: dificuldade = `ln(100/p)`; peso = `max(0.05, dificuldade)^curva`. A ordem é a dificuldade mais um desvio gaussiano de `desordem` vezes a amplitude das dificuldades (uma rara pode vir antes de uma comum e continua demorando o tempo dela). A espera até cada conquista é um espaço sorteado entre `intervalo_minimo_minutos` e `intervalo_maximo_minutos` mais `peso * escala * (1 ± variacao)`, com `escala = (duração - soma dos espaços) / soma dos pesos`, então o total continua sendo a duração. Objetivo: quanto mais rara, mais demora, sem padrão reconhecível na ordem nem no ritmo.

`Planejador.Replanejar`: na primeira sessão (`sincronizado` falso), depois de `Sincronizar`, redistribui só as pendentes a partir de `minutos_jogados`, numa duração igual à fatia do peso delas sobre o peso total. Sem isso, as conquistas que o usuário já tinha ocupavam o começo do cronograma e a primeira pendente demorava horas.

Ao iniciar, a sessão sincroniza com a Steam: conquistas que o usuário já tem viram `ja_tinha`; nomes que não existem no schema viram `ignorada`; conquistas de DLC que a conta não tem viram `sem_dlc` (e voltam a `pendente` se a DLC chegar); com a simulação desligada, as `desbloqueada` com `simulada: true` também voltam a `pendente`. A DLC de cada conquista vem do schema que o cliente guarda em `appcache/stats/UserGameStatsSchema_<appid>.bin` (KeyValues binário, `EsquemaSteam`): com os Achievement Sets, ele tem `groups/<id>/dlcappid` e `groupid` em cada conquista. Não há Web API documentada para isso. A posse vem de `SteamApps008.IsSubscribedApp(dlc)`, que inclui a biblioteca da família. Uma conquista que a Steam recusa 3 vezes (protegida pelo servidor do jogo) também vira `ignorada`.

## Persistência

Estado por jogo em `estado/<appid>.json`, gravado por arquivo temporário + `File.Replace` (troca atômica). Log único, compartilhado entre processos, com `FileShare.ReadWrite` e rotação em 5 MB.
