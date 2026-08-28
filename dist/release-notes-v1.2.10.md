# Orla 1.2.10

Segundo clique confiável e dock mais imediato.

- reconhece como ativo um aplicativo com diálogo, popup ou WebView em foco pelo PID, sem depender apenas do HWND principal;
- diferencia uma janela realmente ativa de um HWND minimizado que o Teams ainda mantém como foreground;
- atualiza a bolinha azul com a mesma regra usada pelo clique;
- envia a minimização antes de recuperar o foco anterior, evitando atraso perceptível;
- detecta o mouse na borda em até 100 ms e conclui a revelação do dock em 160 ms, mantendo a enumeração de sobreposição em cache;
- validado no binário final com 20 cliques físicos, cinco ciclos por monitor, resposta mediana de 93 ms e máxima de 246 ms após o MouseUp;
- validado também com 12 transições pelo canal de acessibilidade, que pode promover momentaneamente a janela WPF do próprio Orla;
- consumo médio observado de 0,661% de CPU total durante 20 segundos, com dois monitores.
