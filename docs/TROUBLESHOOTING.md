# Solução de problemas

## A barra nativa não voltou

```powershell
%LOCALAPPDATA%\Orla\Orla.exe --restore
```

Se o arquivo não existir, reinicie o Explorer pelo Gerenciador de Tarefas ou faça logoff/login.

## Fluent Search não abre ou aparece na tela errada

1. Confirme `FluentSearchPath` em `%LOCALAPPDATA%\Orla\settings.ini`.
2. Confirme `BareWindowsKeyOpensFluent=true`.
3. Clique no botão de busca da tela desejada; Orla pede a abertura pelo canal local do Fluent e só então posiciona a janela nessa tela.
4. Se o processo do Fluent estiver iniciando, aguarde alguns segundos e tente novamente.

Sem Fluent Search instalado, a tecla Windows permanece nativa.

## Um aplicativo elevado não recebe foco

O Windows impede que processos normais controlem janelas elevadas. Execute ambos no mesmo nível. Não inicie Orla como administrador apenas para contornar essa proteção.

## O dock não aparece

Encoste o mouse na última linha de pixels da borda inferior do monitor. A detecção ocorre em até 100 ms e a transição visual termina em 160 ms.

## Wi-Fi, Bluetooth ou bateria mostram um estado inesperado

Os indicadores refletem as APIs do Windows e podem levar até 10 segundos para acompanhar uma mudança externa. Abra o painel pelo ícone de rede, som ou bateria. O Bluetooth continua listado ali mesmo quando está desligado. Clique no corpo do cartão para alternar o rádio; use somente o chevron à direita para abrir sua página nas Configurações do Windows.

Na primeira alternância, o Windows pode solicitar acesso aos rádios. Se uma política corporativa negar `Windows.Devices.Radios`, o cartão permanece no estado confirmado pela releitura e as Configurações continuam disponíveis pelo chevron. Orla não altera a política.

O Wi-Fi mostra intensidade e, quando permitido, o nome da conexão ativa. Se a privacidade ou uma política do Windows restringir o nome, o painel mantém somente o sinal. Sem conexão ativa, o ícone e o texto mudam para o estado desconectado. Computadores sem bateria informada pelo firmware mostram alimentação externa.

## O brilho funciona no notebook, mas não no monitor externo

O monitor externo precisa oferecer DDC/CI e permitir esse comando no modo de imagem atual. No menu físico do monitor:

1. habilite DDC/CI, quando houver essa opção;
2. desative **Eye Saver Mode** e modos equivalentes de proteção ocular;
3. desative economia de energia automática e contraste dinâmico;
4. use um modo de imagem padrão ou personalizado e abra novamente os controles da Orla.

Alguns monitores anunciam suporte DDC/CI mesmo quando um desses modos bloqueia temporariamente a alteração. A tela integrada do notebook usa a interface WMI do Windows e não depende dessas opções do monitor externo.

## Alterei o INI e nada mudou

Saia pelo menu de contexto, edite `%LOCALAPPDATA%\Orla\settings.ini` e inicie novamente.

## Logs

`%LOCALAPPDATA%\Orla\Orla.log`
