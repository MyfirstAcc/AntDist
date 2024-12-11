import sqlite3
import pandas as pd
import matplotlib.pyplot as plt

# Подключение к базе данных SQLite
conn = sqlite3.connect('testsAnts.db')  # Укажите путь к вашей базе данных

# SQL-запрос для извлечения данных
query = """
SELECT
    GROUP_CONCAT(
        CASE 
            WHEN TestParameters.ParameterName = 'NumClients' THEN TestParameters.ParameterValue
            ELSE NULL 
        END, ', '
    ) AS NumClients,
    TestResults.BestValue,
    TestResults.MethodRunTime,
    TestResults.TotalRunTime - TestResults.MethodRunTime As StartTimeClient  
FROM 
    TestRuns
JOIN 
    TestParameters ON TestRuns.Id = TestParameters.TestRunId
JOIN
    TestResults ON TestRuns.Id = TestResults.TestRunId
GROUP BY 
    TestRuns.Id, TestResults.BestValue, TestResults.MethodRunTime;
"""

# Выполнение запроса и загрузка результатов в DataFrame
df = pd.read_sql_query(query, conn)

# Закрытие соединения с базой данных
conn.close()

# Преобразуем строковые данные в списки для параметра NumClients (если необходимо)
df["NumClients"] = df["NumClients"].apply(lambda x: [int(i) for i in x.split(', ')] if x else [])

# Разворачиваем данные, чтобы "NumClients" было представлено как отдельные строки
df_expanded = df.explode("NumClients")

# Группируем данные по количеству клиентов и вычисляем средние значения
df_grouped = df_expanded.groupby("NumClients").mean()
# Построение графика
fig, ax1 = plt.subplots(figsize=(6, 4))

# График для BestValue
color = 'tab:blue'
ax1.set_xlabel('Number of Clients')
ax1.set_ylabel('Best Value', color=color)
ax1.plot(df_grouped.index, df_grouped["BestValue"], marker='o', color=color, label="Best Value")
ax1.tick_params(axis='y', labelcolor=color)

# Вторая ось Y для времени выполнения
ax2 = ax1.twinx()
color = 'tab:orange'
ax2.set_ylabel('Method Run Time (s)', color=color)
ax2.plot(df_grouped.index, df_grouped["MethodRunTime"], marker='s', linestyle='--', color=color, label="Method Run Time")
ax2.tick_params(axis='y', labelcolor=color)





# Добавляем легенду и заголовок
fig.tight_layout()
plt.title("Время выполнения и лучший результат vs Количество клиентов 20Cl 20Ants 200I")




fig2, ax12 = plt.subplots(figsize=(6, 4))
color = 'tab:blue'
ax12.set_xlabel('Number of Clients')
ax12.set_ylabel('Start Client Procces(s)', color=color)
ax12.plot(df_grouped.index, df_grouped["StartTimeClient"], marker='o', color=color, label="Start Client Procces(s)")
ax12.tick_params(axis='y', labelcolor=color)

fig2.tight_layout()
plt.show()