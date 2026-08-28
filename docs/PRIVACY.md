# Privacidade

Orla foi projetada para funcionar localmente e não contém telemetria, analytics,
conta, sincronização em nuvem ou envio automático de relatórios.

## Dados lidos

- janelas, processos e monitor em uso para montar o dock;
- estado de rede, intensidade do Wi-Fi e nome da conexão ativa quando permitido pelo Windows;
- volume, mute, bateria, Bluetooth, luz noturna e economia de energia;
- brilho da tela integrada por WMI e de monitores externos por DDC/CI;
- idioma de exibição e formato regional do Windows.

Esses dados são usados somente na interface local. O nome da rede pode aparecer
temporariamente no painel, mas não é gravado pela Orla.

## Dados armazenados

`%LOCALAPPDATA%\Orla\settings.ini` guarda somente preferências, caminho opcional
do Fluent Search, favoritos e ordem do dock. Logs de diagnóstico ficam em
`%LOCALAPPDATA%\Orla\Orla.log` e permanecem no computador.

## Comunicação

A integração com Fluent Search usa um named pipe local. Orla não inicia conexões
de rede próprias. Links para Configurações do Windows, documentação ou GitHub só
são abertos após uma ação explícita do usuário.

## Remoção

`Uninstall.ps1` remove o executável, a inicialização automática e, por padrão,
os dados locais. Use `-KeepSettings` somente se quiser preservar configuração e log.
