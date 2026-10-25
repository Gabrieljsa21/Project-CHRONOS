# Arquitetura

## Alvo: .NET Framework 4.8, x86

A `SAM.API.dll` é um assembly .NET Framework 4.8 de 32 bits que conversa com a `steamclient.dll`. O Chronos segue o mesmo alvo para referenciá-la direto. A DLL não é copiada para a saída: em tempo de execução, o `AssemblyResolve` a carrega da `pasta_sam` do `config.json`.

## Dois tipos de processo

- **Agendador** (`Chronos.exe` sem argumentos): a cada 30 s relê o `config.json`, cria planos que faltam e decide quais jogos ficam abertos (janelas de horário, `jogos_simultaneos`, ordem da lista). Um mutex `Local\Project-CHRONOS` garante instância única.
- **Sessão** (`Chronos.exe --sessao <appid> --pai <pid>`): um processo por jogo, porque a SAM.API só se conecta a um appid por processo. Enquanto o processo vive, a Steam mostra o jogo como aberto e conta horas. A sessão sai sozinha quando o agendador some; o agendador mata a sessão quando ela sai da janela.

Códigos de saída da sessão: 0 ok, 2 sem estado, 3 falha ao conectar na Steam, 4 Steam não devolveu as estatísticas. Qualquer código diferente de 0 bloqueia o jogo por 5 min antes de nova tentativa.

## Relógio próprio

O Chronos conta o tempo jogado por conta própria (`minutos_jogados` no estado), em vez de ler as horas da Steam. Cada ciclo de 60 s soma no máximo 2 min, para não contar como jogado o tempo em que o PC hibernou. A variável de ambiente `CHRONOS_ACELERAR` multiplica o relógio, apenas para testes.

## Cronograma

`Planejador.DistribuirTempos`: posição = `0.02 + 0.98 * (ln(pTopo/p) / ln(pTopo/pBase))^curva`, vezes a duração, com ruído de ±`variacao`. Os tempos são reordenados (o ruído pode inverter vizinhas) e separados por pelo menos `intervalo_minimo_minutos`.

Ao iniciar, a sessão sincroniza com a Steam: conquistas que o usuário já tem viram `ja_tinha`; nomes que não existem no schema viram `ignorada`. Uma conquista que a Steam recusa 3 vezes (protegida pelo servidor do jogo) também vira `ignorada`.

## Persistência

Estado por jogo em `estado/<appid>.json`, gravado por arquivo temporário + `File.Replace` (troca atômica). Log único, compartilhado entre processos, com `FileShare.ReadWrite` e rotação em 5 MB.
