﻿using System;
using AccidentalFish.ApplicationSupport.Core.Configuration;
using AccidentalFish.ApplicationSupport.Core.Repository;
using AccidentalFish.ApplicationSupport.Repository.EntityFramework.Policies;

namespace AccidentalFish.ApplicationSupport.Repository.EntityFramework.Repository
{
    class EntityFrameworkUnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;
        private readonly IDbContextFactory _contextFactory;
        private readonly IDbConfiguration _dbConfiguration;
        private readonly Type _contextType;

        public EntityFrameworkUnitOfWorkFactory(IConfiguration configuration, IDbContextFactory contextFactory, IDbConfiguration dbConfiguration)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");
            if (contextFactory == null) throw new ArgumentNullException("contextFactory");
            if (dbConfiguration == null) throw new ArgumentNullException("dbConfiguration");

            _configuration = configuration;
            _contextFactory = contextFactory;
            _dbConfiguration = dbConfiguration;
            _contextType = null;
        }

        public EntityFrameworkUnitOfWorkFactory(
            string contextType,
            string connectionString,
            IDbConfiguration dbConfiguration)
        {
            if (String.IsNullOrWhiteSpace(contextType)) throw new ArgumentNullException("contextType");
            if (String.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException("connectionString");
            if (dbConfiguration == null) throw new ArgumentNullException("dbConfiguration");


            _contextType = Type.GetType(contextType);
            _connectionString = connectionString;
            _dbConfiguration = dbConfiguration;

            if (_contextType == null)
            {
                throw new InvalidOperationException(String.Format("Unable to locate context type {0}", _contextType));
            }
        }

        public IUnitOfWork Create()
        {
            return _contextType == null ?
                new EntityFrameworkUnitOfWork(_configuration, _contextFactory, _dbConfiguration) :
                new EntityFrameworkUnitOfWork(_contextType, _connectionString, _dbConfiguration);
        }

        public IUnitOfWorkAsync CreateAsync()
        {
            return _contextType == null ?
                new EntityFrameworkUnitOfWorkAsync(_configuration, _contextFactory, _dbConfiguration) :
                new EntityFrameworkUnitOfWorkAsync(_contextType, _connectionString, _dbConfiguration);
        }
    }
}
