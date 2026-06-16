#pragma once

#ifndef CHATLISTMODEL_H
#define CHATLISTMODEL_H

#include <QAbstractListModel>
#include <QDateTime>
#include <QVector>
#include <QString>

struct ChatItem {
    QString chatId;
    QString title;
    QString lastMessage;
    QDateTime lastTime;
};

class ChatListModel : public QAbstractListModel
{
    Q_OBJECT
public:
    explicit ChatListModel(QObject* parent = nullptr);  
    enum Roles {
        ChatIdRole = Qt::UserRole + 1,
        TitleRole,
        LastMessageRole,
        LastTimeRole
    };

    int rowCount(const QModelIndex&) const override;
    QVariant data(const QModelIndex&, int role) const override;

    void setChats(const QVector<ChatItem>& chats);
    Qt::ItemFlags flags(const QModelIndex &index) const override;
    Qt::DropActions supportedDropActions() const override;
    bool moveRows(const QModelIndex &sourceParent, int sourceRow, int count,
                  const QModelIndex &destinationParent, int destinationRow) override;
    bool dropMimeData(const QMimeData *data, Qt::DropAction action,
                      int row, int column, const QModelIndex &parent) override;
    QVector<ChatItem> getChats() const { return m_chats; }
    void setChatsOrdered(const QVector<ChatItem>& chats);
    signals:
    void chatOrderChanged();


protected:
    QHash<int, QByteArray> roleNames() const override;

private:
    QVector<ChatItem> m_chats;

    friend class MainWindow;
};

#endif 