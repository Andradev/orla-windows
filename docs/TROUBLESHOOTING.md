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

Encoste o mouse na última linha de pixels da borda inferior do monitor. O dock aparece após aproximadamente 100–500 ms, conforme o ciclo de detecção.

## Wi-Fi, Bluetooth ou bateria mostram um estado inesperado

Os indicadores refletem as APIs do Windows e podem levar até 10 segundos para acompanhar uma mudança. Abra o painel pelo ícone de rede, som ou bateria. O Bluetooth continua listado ali mesmo quando está desligado; clique no cartão para abrir diretamente sua página nas Configurações do Windows.

O Wi-Fi mostra intensidade sem ler o nome da rede. Se não houver conexão ativa, o ícone e o texto mudam para o estado desconectado. Computadores sem bateria informada pelo firmware mostram alimentação externa.

## Alterei o INI e nada mudou

Saia pelo menu de contexto, edite `%LOCALAPPDATA%\Orla\settings.ini` e inicie novamente.

## Logs

`%LOCALAPPDATA%\Orla\Orla.log`
