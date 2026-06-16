#pragma once

#ifndef BACKENDMANAGER_H
#define BACKENDMANAGER_H

#include <QObject>
#include <QProcess>
#include <QTimer>
#include <QString>

class BackendManager : public QObject
{
    Q_OBJECT
    Q_PROPERTY(bool isRunning READ isRunning NOTIFY runningStateChanged)
    Q_PROPERTY(bool isReady READ isReady NOTIFY readyStateChanged)

public:
    explicit BackendManager(QObject* parent = nullptr);
    ~BackendManager();

    bool isRunning() const { return m_process && m_process->state() == QProcess::Running; }
    bool isReady() const { return m_isReady; }
    QString getBackendPath() const { return m_backendPath; }

    void setBackendPath(const QString& path) { m_backendPath = path; }
    void setStartupDelay(int ms) { m_startupDelay = ms; }
    QProcess* getProcess() const { return m_process; }

public slots:
    void startBackend();
    void stopBackend();
    void restartBackend();
    void sendCommand(const QString& command, int delayMs = 0);
    void sendCommands(const QStringList& commands, const QList<int>& delays = QList<int>());

signals:
    void runningStateChanged(bool running);
    void readyStateChanged(bool ready);
    void backendStarted();
    void backendStopped();
    void backendError(const QString& error);
    void commandSent(const QString& command);
    void allCommandsCompleted();
    void consoleOutput(const QString& text);


private slots:
    void onProcessStarted();
    void onProcessFinished(int exitCode, QProcess::ExitStatus exitStatus);
    void onProcessError(QProcess::ProcessError error);
    void onProcessReadyRead();
    void executeNextCommand();

private:
    void checkReadiness();
    void cleanupProcess();

    QProcess* m_process = nullptr;
    QString m_backendPath;
    QStringList m_commandQueue;
    QList<int> m_delays;
    QTimer* m_commandTimer = nullptr;
    bool m_isReady = false;
    int m_startupDelay = 2000;
};

#endif 
