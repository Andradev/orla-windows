Correções de confiabilidade do Fluent Search e do dock:

- toques rápidos em `Win` são serializados e não descartados;
- o comando nativo confirma tanto a abertura quanto o fechamento sem bloquear a próxima solicitação;
- o dock localiza novamente a janela atual do aplicativo no momento do clique;
- ativações momentaneamente recusadas pelo Windows recebem tentativas curtas e limitadas;
- mantém o foco anterior, o suporte a dois monitores, a posição estável e a lista vazia de fixos.

Validado no Windows 11 com alternâncias rápidas do Fluent Search e 12 ciclos de minimização/restauração do Teams. Todos os 12 ciclos ativaram a janela correta em 0–117 ms.
