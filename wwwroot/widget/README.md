# Локальная копия Grist-виджета (только для отладки)

Здесь лежит **байт-в-байт копия** `index.html` из репозитория
https://github.com/Altair666/widget_report_grist (ветка master).

Зачем копия, если есть GitHub Pages:
GitHub Pages отдаёт виджет по **https**, а AltiumSync слушает **http://…:5000**.
Браузер блокирует запрос с https-страницы на http-адрес как mixed content,
поэтому кнопка «Синхронизация» с Pages в локальной отладке не работает в принципе.
Копия, отданная самим AltiumSync, живёт на том же origin, что и API — блокировки нет.

В Grist (Custom URL виджета) для отладки указывать:
    http://<этот-сервер>:5000/widget/

Правки виджета делаются **в его собственном репозитории**, не здесь.
Обновить копию после изменений в репозитории:
    copy C:\widget_report_grist\index.html C:\gg1ac\wwwroot\widget\index.html
