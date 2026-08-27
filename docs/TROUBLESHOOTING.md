# Solução de problemas

## A barra nativa não voltou

```powershell
%LOCALAPPDATA%\VictorShell\VictorShell.exe --restore
```

Se o executável não existir, reinicie o Explorer pelo Gerenciador de Tarefas ou faça logoff/login.

## A tecla Windows não abre o Fluent Search

Confirme:

1. `FluentSearchPath` aponta para o executável correto;
2. o atalho em `FluentSearchHotkey` corresponde ao configurado no Fluent Search;
3. `BareWindowsKeyOpensFluent=true`;
4. o app em foco não está elevado enquanto VictorShell não está.

Sem Fluent Search instalado, a tecla Windows fica nativa por segurança.

## Um aplicativo elevado não recebe foco

O Windows impede que processos normais controlem janelas elevadas. Execute ambos no mesmo nível. Não é recomendado iniciar VictorShell como administrador apenas para contornar isso.

## O dock não aparece

Encoste o mouse na última linha de pixels da borda inferior do monitor. O dock leva aproximadamente 100–500 ms para aparecer, dependendo do ciclo de detecção.

## Alterei o INI e nada mudou

Saia pelo menu de contexto, edite `%LOCALAPPDATA%\VictorShell\settings.ini` e inicie novamente.

## Logs

`%LOCALAPPDATA%\VictorShell\VictorShell.log`
