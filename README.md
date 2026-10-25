# Project CHRONOS

Desbloqueia conquistas da Steam aos poucos, sem interação, num ritmo parecido com o de quem joga de verdade. Usa a `SAM.API.dll` do [Steam Achievement Manager](https://github.com/gibbed/SteamAchievementManager) 7.x.

## Como funciona

1. Você lista os jogos no `config.json`.
2. Para cada jogo, o Chronos monta um cronograma com dados públicos do [SteamHunters](https://steamhunters.com):
   - **duração total:** o tempo mediano para 100% do jogo (sem esse dado, usa `duracao_padrao_horas`);
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

O log fica em `%APPDATA%\Chronos\chronos.log` e o estado de cada jogo em `%APPDATA%\Chronos\estado\<appid>.json`. Para refazer o plano de um jogo, apague o arquivo de estado dele.

## Riscos

Alterar conquistas em jogos online ou com anti-trapaça (VAC e similares) pode ser detectado e gerar punição. Prefira jogos single-player offline.
