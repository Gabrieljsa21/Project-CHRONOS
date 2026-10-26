# Project CHRONOS

Desbloqueia conquistas da Steam aos poucos, sem interação, num ritmo parecido com o de quem joga de verdade. Usa a `SAM.API.dll` do [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) 7.x.

## Como funciona

1. Você lista os jogos no `config.json`.
2. Para cada jogo, o Chronos monta um cronograma com dados públicos do [SteamHunters](https://steamhunters.com):
   - **duração total:** o tempo mediano para 100% do jogo (sem esse dado, usa `duracao_padrao_horas`, 50 h);
   - **ordem:** da conquista mais comum para a mais rara;
   - **momento de cada uma:** proporcional à raridade, em escala logarítmica, com variação aleatória;
   - conquistas marcadas como impossíveis de obter ficam de fora.
   Se o SteamHunters falhar, usa as porcentagens globais da Steam.
3. Dentro das janelas de horário configuradas, o Chronos "abre" o jogo na Steam (o perfil mostra "jogando" e conta horas) e desbloqueia cada conquista quando o tempo jogado alcança o minuto planejado, no máximo uma por minuto.
4. O progresso fica salvo; depois de reiniciar o PC, continua de onde parou.

## Requisitos

- Windows, Steam aberta e logada na conta dona dos jogos.
- .NET Framework 4.8 (já vem no Windows 10/11) e o SDK do .NET para compilar.
- Steam Achievement Manager 7.x baixado (só a pasta com a `SAM.API.dll` é usada).

## Instalação

```powershell
dotnet build -c Release                              # gera bin\Release\net48\Chronos.exe
powershell -File scripts\instalar_inicializacao.ps1  # opcional: sobe sozinho no login
```

Na primeira execução, o Chronos cria `%APPDATA%\Chronos\config.json` a partir do `config.exemplo.json`. Ajuste a lista de jogos e os horários ali. O arquivo é relido a cada 30 segundos, então não é preciso reiniciar.

O exemplo vem com `"simulacao": true`: a sessão abre na Steam e conta horas, mas só registra no log o que desbloquearia. Troque para `false` quando o plano estiver como você quer.

## Comandos

| Comando | O que faz |
|---|---|
| `Chronos.exe` | Agendador em segundo plano, sem janela. Só uma instância roda por vez. |
| `Chronos.exe --status` | Progresso de cada jogo e a próxima conquista. |
| `Chronos.exe --plano <appid>` | Mostra um cronograma de exemplo, sem salvar nada. |
| `Chronos.exe --biblioteca` | Lista em JSON todos os jogos que a conta pode abrir, inclusive os da família. Precisa da Steam aberta. |

O log fica em `%APPDATA%\Chronos\chronos.log` e o estado de cada jogo em `%APPDATA%\Chronos\estado\<appid>.json`. Para refazer o plano de um jogo, apague o arquivo de estado dele.

## Discord

Com `"manter_aberto": true`, um jogo da lista que não tem mais conquistas a liberar (concluído, ou sem conquistas na Steam) continua aberto na Steam só para contar horas, como o ArchiSteamFarm faz. Os jogos com conquistas a liberar têm prioridade nas vagas de `jogos_simultaneos`. Esses jogos não aparecem como "Jogando" no Discord. Se um jogo sem conquistas ganhar conquistas, a sessão reabre no modo normal.

Enquanto um jogo está aberto, o Discord mostra "Jogando <jogo>" com o ícone oficial (Rich Presence, precisa do Discord aberto; se ele abrir depois, a sessão conecta no minuto seguinte). Jogos fora da lista de jogos do Discord ficam sem atividade. Desliga com `"discord": false` no config.

## Ordem e DLC

- Quanto mais rara a conquista, mais demora: a espera até cada uma é um espaço sorteado entre `intervalo_minimo_minutos` e `intervalo_maximo_minutos` (15 e 30) mais uma fatia da duração proporcional à dificuldade. O intervalo entre duas conquistas varia, sem ritmo fixo; a ordem vai da mais comum para a mais rara (`desordem` acima de 0 embaralha um pouco, 0 por padrão).
- Na primeira sessão, o cronograma é refeito só com as conquistas que faltam, a partir do ponto em que você está.
- Conquistas de DLC que a conta (ou a família) não tem ficam de fora quando o jogo usa os Achievement Sets da Steam, que marcam a DLC de cada conquista. Em jogos que ainda não usam sets, a Steam não informa isso.

## Riscos

Alterar conquistas em jogos online ou com anti-trapaça (VAC e similares) pode ser detectado e gerar punição. Prefira jogos single-player offline.
