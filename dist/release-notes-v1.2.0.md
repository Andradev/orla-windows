# Orla 1.2.0

Esta versão transforma os ícones da topbar em indicadores funcionais e adiciona um painel rápido leve e acessível.

- Wi-Fi com intensidade real de 0–100%, ícone por faixa de sinal e estado desconectado explícito;
- volume e mute sincronizados por callback nativo, com slider funcional no painel;
- bateria com percentual, carga, alimentação externa e tempo restante quando o Windows fornece esse dado;
- Bluetooth sempre configurável no painel e visível na topbar somente quando habilitado;
- cartões de rede, Bluetooth e energia abrem diretamente as páginas correspondentes das Configurações do Windows;
- idioma automático pelo perfil do Windows: português, inglês e espanhol, com fallback em inglês;
- layout do painel padronizado, navegação por teclado e nomes acessíveis para UI Automation;
- um monitor de estado compartilhado entre as duas telas, com eventos para rede/áudio e polling lento para os demais sensores;
- pedidos rápidos do Fluent Search continuam serializados, e o dock preserva o foco corretamente nas duas telas.

Validado no Windows 11 com dois monitores: painel em ambas as telas, seis ciclos de stress sem vazamento progressivo, oito trocas de foco no dock e três alternâncias rápidas do Fluent Search. O binário continua sendo um único executável WPF x64 sem dependências externas.

Medição limpa de 60 segundos: 0,746% de CPU média, 74,37 MiB de working set e 63,59 MiB privados.
